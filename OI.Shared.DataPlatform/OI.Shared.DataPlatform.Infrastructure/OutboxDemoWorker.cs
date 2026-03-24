using System.Text.Json;
using Avro;
using Avro.Generic;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OI.Messaging.Kafka.Outbox;
using OI.Shared.DataPlatform.Infrastructure.Database;
using OI.Shared.DataPlatform.Infrastructure.Domain;
using NpgsqlTypes;

namespace OI.Shared.DataPlatform.Infrastructure;

/// <summary>
/// Background worker that inserts a fake PDUReading into the outbox every 5 seconds as Avro bytes.
/// </summary>
public sealed class OutboxDemoWorker(
    IDbConnectionFactory connectionFactory,
    OutboxSerializer outboxSerializer,
    ILogger<OutboxDemoWorker> logger) : BackgroundService
{
    private const string Topic = "pdu-readings-avro";

    private const string InsertOutboxSql = """
        INSERT INTO outbox (id, aggregate_type, aggregate_id, type, payload)
        VALUES (@Id, @AggregateType, @AggregateId, @Type, @Payload::bytea)
        """;

    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDemoWorker (Avro byte[]) started — inserting every 5s");

        string schemaStr = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AvroSchemas", "pdu_reading.avsc"));
        var schema = (RecordSchema)Schema.Parse(schemaStr);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _counter++;
                string payloadId = $"pdu-avro-{_counter:D5}";

                // We construct a GenericRecord that matches our Avro schema
                var record = new GenericRecord(schema);
                record.Add("id", payloadId);
                record.Add("message", $"This is a simple test message for AVRO outbox #{_counter}");

                // Serialize to Confluent Avro wire format (Magic Byte + Schema ID + Payload)
                // We use the final routed topic name here so the schema is registered for the correct subject.
                byte[] avroBytes = await outboxSerializer.SerializeAvroAsync(record, $"{Topic}.events");

                var outboxMsg = new
                {
                    Id = Guid.NewGuid(),
                    AggregateType = Topic,
                    AggregateId = payloadId,
                    Type = "PDUReadingAvro",
                    Payload = avroBytes
                };

                using var conn = connectionFactory.CreateConnection();
                await conn.OpenAsync(stoppingToken);
                await conn.ExecuteAsync(new CommandDefinition(InsertOutboxSql, outboxMsg, cancellationToken: stoppingToken));

                logger.LogInformation("Outbox (Avro) #{Counter} inserted — topic={Topic}", _counter, Topic);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to insert outbox message (Avro)");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
