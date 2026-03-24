using Dapper;
using OI.Shared.DataPlatform.Infrastructure;
using OI.Shared.DataPlatform.Infrastructure.Database;

DefaultTypeMap.MatchNamesWithUnderscores = true;
SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DataPlatform")!;
var schemaRegistryUrl = builder.Configuration["Kafka:Connection:SchemaRegistryUrl"]!;

builder.Services.AddDataPlatformInfrastructure(connectionString, schemaRegistryUrl);
builder.Services.AddHostedService<OutboxDemoWorker>();

builder.Services.AddOpenApi();

var app = builder.Build();

// 1. Run DB migrations
app.Services.GetRequiredService<DatabaseMigrator>().MigrateUp();

// 2. Register Avro schemas (idempotent)
var schemaRegistrar = app.Services.GetRequiredService<AvroSchemaRegistrar>();
var schemaPath = Path.Combine(AppContext.BaseDirectory, "AvroSchemas", "pdu_reading.avsc");
await schemaRegistrar.RegisterSchemaAsync("pdu-readings-avro.events-value", schemaPath);

// // 2. Register Debezium connector (local dev — idempotent PUT, non-fatal on failure)
// var kafkaConnectUrl = builder.Configuration["Kafka:Connection:KafkaConnectUrl"];
// if (!string.IsNullOrEmpty(kafkaConnectUrl))
// {
//     var registrar = app.Services.GetRequiredService<DebeziumConnectorRegistrar>();
//     var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
//         "eng", "manifests-kraft", "connector-config.json");
//     await registrar.EnsureConnectorAsync(kafkaConnectUrl, configPath);
// }

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).WithName("Health");

app.Run();

public partial class Program { }
