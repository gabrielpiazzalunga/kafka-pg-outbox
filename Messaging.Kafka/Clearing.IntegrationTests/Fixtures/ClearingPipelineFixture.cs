using System.Net;
using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace Clearing.IntegrationTests.Fixtures;

/// <summary>
/// Shared xUnit fixture that spins up the full clearing pipeline infrastructure:
/// Postgres (with WAL_LEVEL=logical), Kafka (KRaft), Schema Registry, and Debezium Kafka Connect.
/// The Debezium connector is auto-registered via the Connect REST API after startup.
/// </summary>
public class ClearingPipelineFixture : IAsyncLifetime
{
    // --- Containers ---
    private INetwork _network = null!;
    
    public PostgreSqlContainer Postgres { get; private set; } = null!;
    public KafkaContainer Kafka { get; private set; } = null!;
    public IContainer SchemaRegistry { get; private set; } = null!;
    public IContainer KafkaConnect { get; private set; } = null!;

    // --- Connection Strings ---
    public string PostgresConnectionString => Postgres.GetConnectionString();
    
    // Kafka broker address as seen from the HOST (for .NET consumer)
    public string KafkaBootstrapServers => Kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        // 1. Create a shared Docker network so containers can resolve each other by name
        _network = new NetworkBuilder()
            .WithName($"clearing-test-{Guid.NewGuid():N}")
            .Build();
        await _network.CreateAsync();

        // 2. PostgreSQL with WAL_LEVEL=logical (required for Debezium pgoutput)
        Postgres = new PostgreSqlBuilder("postgres:16")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres")
            .WithDatabase("ledger")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .WithCommand("-c", "wal_level=logical")
            .Build();

        // 3. Kafka (KRaft mode, no Zookeeper needed)
        Kafka = new KafkaBuilder("confluentinc/cp-kafka:7.6.0")
            .WithNetwork(_network)
            .WithNetworkAliases("kafka")
            .Build();

        // Start Postgres and Kafka in parallel (they are independent)
        await Task.WhenAll(Postgres.StartAsync(), Kafka.StartAsync());

        // 4. Confluent Schema Registry
        SchemaRegistry = new ContainerBuilder("confluentinc/cp-schema-registry:7.6.0")
            .WithNetwork(_network)
            .WithNetworkAliases("schema-registry")
            .WithPortBinding(8081, true)
            .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS", "kafka:9093")
            .WithEnvironment("SCHEMA_REGISTRY_LISTENERS", "http://0.0.0.0:8081")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8081).ForPath("/")))
            .Build();

        await SchemaRegistry.StartAsync();

        // 5. Debezium Kafka Connect
        KafkaConnect = new ContainerBuilder("debezium/connect:2.5")
            .WithNetwork(_network)
            .WithNetworkAliases("kafka-connect")
            .WithPortBinding(8083, true)
            .WithEnvironment("BOOTSTRAP_SERVERS", "kafka:9093")
            .WithEnvironment("GROUP_ID", "clearing-connect-test")
            .WithEnvironment("CONFIG_STORAGE_TOPIC", "connect_configs")
            .WithEnvironment("OFFSET_STORAGE_TOPIC", "connect_offsets")
            .WithEnvironment("STATUS_STORAGE_TOPIC", "connect_statuses")
            .WithEnvironment("KEY_CONVERTER", "org.apache.kafka.connect.storage.StringConverter")
            .WithEnvironment("VALUE_CONVERTER", "org.apache.kafka.connect.json.JsonConverter")
            .WithEnvironment("VALUE_CONVERTER_SCHEMAS_ENABLE", "false")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8083).ForPath("/connectors")))
            .Build();

        await KafkaConnect.StartAsync();

        // 6. Apply DB Migrations (create tables)
        await ApplyMigrationsAsync();

        // 7. Register Debezium connector via REST API
        await RegisterDebeziumConnectorAsync();
    }

    private async Task ApplyMigrationsAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();

        // Read all migration files in order and apply them
        var migrationsDir = FindMigrationsDirectory();
        var migrationFiles = Directory.GetFiles(migrationsDir, "*.sql").OrderBy(f => f).ToList();

        foreach (var file in migrationFiles)
        {
            var sql = await File.ReadAllTextAsync(file);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static string FindMigrationsDirectory()
    {
        // Walk up from the test assembly to find the Ledger.Service/Infrastructure/Migrations folder
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Ledger.Service", "Infrastructure", "Migrations");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find Ledger.Service/Infrastructure/Migrations directory.");
    }

    private async Task RegisterDebeziumConnectorAsync()
    {
        var connectPort = KafkaConnect.GetMappedPublicPort(8083);
        var baseUrl = $"http://localhost:{connectPort}";
        
        using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };

        // The connector config for our test — uses simplified JSON converter (no Schema Registry needed for this test)
        var connectorJson = """
        {
          "name": "clearing-ledger-connector",
          "config": {
            "connector.class": "io.debezium.connector.postgresql.PostgresConnector",
            "database.hostname": "postgres",
            "database.port": "5432",
            "database.user": "postgres",
            "database.password": "postgres",
            "database.dbname": "ledger",
            "topic.prefix": "ledger",
            "slot.name": "debezium_test_slot",
            "publication.name": "debezium_test_pub",
            "plugin.name": "pgoutput",
            "table.include.list": "public.journal_entries",
            "snapshot.mode": "initial",
            "tombstones.on.delete": "false",
            "transforms": "unwrap,route",
            "transforms.unwrap.type": "io.debezium.transforms.ExtractNewRecordState",
            "transforms.unwrap.drop.tombstones": "true",
            "transforms.unwrap.delete.handling.mode": "drop",
            "transforms.route.type": "org.apache.kafka.connect.transforms.RegexRouter",
            "transforms.route.regex": "ledger.public.(.*)",
            "transforms.route.replacement": "ledger.journal.events",
            "key.converter": "org.apache.kafka.connect.storage.StringConverter",
            "value.converter": "org.apache.kafka.connect.json.JsonConverter",
            "value.converter.schemas.enable": "false",
            "errors.tolerance": "none"
          }
        }
        """;

        var content = new StringContent(connectorJson, Encoding.UTF8, "application/json");
        
        // Retry a few times — Connect might need a moment to fully initialize
        for (int i = 0; i < 5; i++)
        {
            var response = await httpClient.PostAsync("/connectors", content);
            if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.Conflict)
            {
                return; // Connector registered (or already exists)
            }
            await Task.Delay(2000);
        }

        throw new Exception("Failed to register Debezium connector after 5 retries.");
    }

    public async Task DisposeAsync()
    {
        if (KafkaConnect != null) await KafkaConnect.DisposeAsync();
        if (SchemaRegistry != null) await SchemaRegistry.DisposeAsync();
        if (Kafka != null) await Kafka.DisposeAsync();
        if (Postgres != null) await Postgres.DisposeAsync();
        if (_network != null) await _network.DeleteAsync();
    }
}
