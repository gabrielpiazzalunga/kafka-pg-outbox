using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using FeedIngestion.Service;
using FeedIngestion.Service.Infrastructure.Database;
using Messaging.Kafka;
using Messaging.Kafka.Options;
using Messaging.Kafka.Options.Resilience;
using Messaging.Kafka.Outbox;

// ── CLI argument parsing ──────────────────────────────────────────
string? ingestVisaPath = null;
string? ingestVisaRedisPath = null;
string? generateMockPath = null;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--ingest-visa") ingestVisaPath = args[i + 1];
    if (args[i] == "--ingest-visa-redis") ingestVisaRedisPath = args[i + 1];
    if (args[i] == "--generate-mock") generateMockPath = args[i + 1];
}

// ── Host Setup ────────────────────────────────────────────────────
var host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureServices((context, services) =>
    {
        var cfg = context.Configuration;
        var pgConnectionString = cfg.GetSection("Postgres")["ConnectionString"]!;
        var bootstrapServers = cfg.GetSection("Kafka:Connection")["BootstrapServer"]!;
        var schemaRegistryUrl = cfg.GetSection("Kafka:Connection")["SchemaRegistryUrl"]!;
        var redisConnectionString = cfg.GetSection("Redis")["ConnectionString"] ?? "localhost:6379";

        // PostgreSQL
        services.AddSingleton(new DatabaseMigrator(pgConnectionString));
        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(pgConnectionString));

        // Schema Registry (for OutboxSerializer used by Alt 1)
        services.AddSingleton<Confluent.SchemaRegistry.ISchemaRegistryClient>(
            new Confluent.SchemaRegistry.CachedSchemaRegistryClient(
                new Confluent.SchemaRegistry.SchemaRegistryConfig { Url = schemaRegistryUrl }));
        services.AddSingleton<OutboxSerializer>();

        // Alternative 1: PostgreSQL Outbox (existing)
        services.AddSingleton<FeedIngestionJob>();

        // Alternative 2: Redis + Direct Kafka via IKafkaProducer
        services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddKafkaProducer(
            new KafkaConnectionConfig
            {
                BootstrapServer = bootstrapServers,
                SchemaRegistryUrl = schemaRegistryUrl
            },
            new KafkaProducerConfig
            {
                LingerMs = 5,
                BatchNumMessages = 500,
                ProduceTimeout = 30
            },
            new KafkaResilienceOptions
            {
                Retry = new KafkaResilienceRetryOptions { MaxRetryAttempts = 3, Delay = 500 },
                Timeout = new KafkaResilienceTimeoutOptions { Timeout = 30000 },
                CircuitBreaker = new KafkaResilienceCircuitBreakerOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = 30000,
                    MinimumThroughput = 10,
                    BreakDuration = 15000
                }
            });
        services.AddSingleton<FeedIngestionRedisJob>();
    })
    .Build();

// ── Run Migrations ────────────────────────────────────────────────
var migrator = host.Services.GetRequiredService<DatabaseMigrator>();
migrator.MigrateUp();

// ── Generate Mock File ────────────────────────────────────────────
if (generateMockPath != null)
{
    Console.WriteLine($"Generating mock Visa settlement file at: {generateMockPath}");
    await using var fs = new FileStream(generateMockPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
    await using var writer = new StreamWriter(fs);
    for (int i = 0; i < 500_000; i++)
    {
        await writer.WriteLineAsync(
            $"TC10 RECORD {i:D10} AUTH12 411111XXXXXX1111 00000015000 00000014800 00000000200 840 {DateTime.UtcNow:yyyyMMdd}");
    }
    Console.WriteLine("Mock file generated successfully.");
    return;
}

// ── Ingest Visa File (Alternative 1: PostgreSQL Outbox) ──────────
if (ingestVisaPath != null)
{
    var job = host.Services.GetRequiredService<FeedIngestionJob>();
    string fileId = $"visa_feed_{Path.GetFileNameWithoutExtension(ingestVisaPath)}_{DateTime.UtcNow:yyyyMMddHHmm}";
    await job.IngestVisaFileAsync(fileId, ingestVisaPath, CancellationToken.None);
    return;
}

// ── Ingest Visa File (Alternative 2: Redis + Direct Kafka) ───────
if (ingestVisaRedisPath != null)
{
    var job = host.Services.GetRequiredService<FeedIngestionRedisJob>();
    string fileId = $"visa_feed_{Path.GetFileNameWithoutExtension(ingestVisaRedisPath)}_{DateTime.UtcNow:yyyyMMddHHmm}";
    await job.IngestVisaFileAsync(fileId, ingestVisaRedisPath, CancellationToken.None);
    return;
}

// ── Default: show usage ───────────────────────────────────────────
Console.WriteLine("FeedIngestion.Service — Usage:");
Console.WriteLine("  --generate-mock <path>        Generate a mock Visa settlement file");
Console.WriteLine("  --ingest-visa <path>          Ingest via PostgreSQL Outbox (exactly-once)");
Console.WriteLine("  --ingest-visa-redis <path>    Ingest via Redis + Direct Kafka (at-least-once)");
