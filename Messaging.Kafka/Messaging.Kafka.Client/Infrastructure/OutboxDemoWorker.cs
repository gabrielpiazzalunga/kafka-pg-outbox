using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Messaging.Kafka.Outbox;
using Messaging.Kafka.Client.Infrastructure.Database;
using Messaging.Kafka.Client.Infrastructure.Domain;
using Messaging.Contracts.Proto;

namespace Messaging.Kafka.Client.Infrastructure;

/// <summary>
/// Background worker that inserts a fake SampleEvent into the outbox every 5 seconds as Protobuf bytes.
/// </summary>
public sealed class OutboxDemoWorker(
    IDbConnectionFactory connectionFactory,
    OutboxSerializer outboxSerializer,
    ILogger<OutboxDemoWorker> logger) : BackgroundService
{
    private const string Topic = "sample-events-protobuf";

    private const string InsertOutboxSql = """
        INSERT INTO outbox (id, aggregate_type, aggregate_id, type, payload)
        VALUES (@Id, @AggregateType, @AggregateId, @Type, @Payload::bytea)
        """;

    private int _counter;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxDemoWorker (Protobuf byte[]) started — inserting every 5s");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _counter++;
                string payloadId = $"sample-proto-{_counter:D5}";

                // We construct a Protobuf message that matches our schema
                var record = new SampleEvent
                {
                    Id = payloadId,
                    Message = $"This is a simple test message for PROTOBUF outbox #{_counter}"
                };

                // Serialize to Confluent Protobuf wire format (Magic Byte + Schema ID + Message Indexes + Payload)
                // We use the final routed topic name here so the schema is registered for the correct subject.
                byte[] protobufBytes = await outboxSerializer.SerializeProtobufAsync(record, $"{Topic}.events");

                var outboxMsg = new
                {
                    Id = Guid.NewGuid(),
                    AggregateType = Topic,
                    AggregateId = payloadId,
                    Type = "SampleEvent",
                    Payload = protobufBytes
                };

                using var conn = connectionFactory.CreateConnection();
                await conn.OpenAsync(stoppingToken);
                await conn.ExecuteAsync(new CommandDefinition(InsertOutboxSql, outboxMsg, cancellationToken: stoppingToken));

                logger.LogInformation("Outbox (Protobuf) #{Counter} inserted — topic={Topic}", _counter, Topic);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to insert outbox message (Protobuf)");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
