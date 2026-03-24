using Confluent.SchemaRegistry;
using Microsoft.Extensions.Logging;

namespace OI.Shared.DataPlatform.Infrastructure.Database;

/// <summary>
/// Service that registers Avro schemas to the Schema Registry at application startup.
/// </summary>
public sealed class AvroSchemaRegistrar(
    ISchemaRegistryClient schemaRegistry,
    ILogger<AvroSchemaRegistrar> logger)
{
    public async Task RegisterSchemaAsync(string subject, string schemaPath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(schemaPath))
            {
                logger.LogError("Avro schema file not found at {Path}", schemaPath);
                return;
            }

            var schemaText = await File.ReadAllTextAsync(schemaPath, ct);
            
            // Register return the schema ID (either existing or new)
            var schemaId = await schemaRegistry.RegisterSchemaAsync(subject, new Schema(schemaText, SchemaType.Avro));
            
            logger.LogInformation("Schema registered successfully for subject {Subject} (ID: {Id})", subject, schemaId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to register Avro schema for subject {Subject}", subject);
        }
    }
}
