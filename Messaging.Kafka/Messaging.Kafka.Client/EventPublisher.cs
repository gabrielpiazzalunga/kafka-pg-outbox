using System.Diagnostics;
using Dapper;
using Messaging.Contracts.Proto;
using Messaging.Kafka.Client.Infrastructure.Database;
using Messaging.Kafka.Outbox;

namespace Messaging.Kafka.Client;

internal sealed class EventPublisher(
    IDbConnectionFactory connectionFactory,
    OutboxSerializer outboxSerializer)
{
    private const string Topic = "sample-events-protobuf";
    private const string InsertOutboxSql = """
        INSERT INTO outbox (id, aggregate_type, aggregate_id, type, payload)
        VALUES (@Id, @AggregateType, @AggregateId, @Type, @Payload::bytea)
        """;

    public async Task RunLoadTestAsync(int total, LoadTestConsumeTracker tracker, CancellationToken ct)
    {
        Console.WriteLine($"Load test (Outbox): {total} messages → '{Topic}'");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        Console.WriteLine("Sending warmup message via Outbox — waiting for Debezium to pick it up...");
        var warmupTask = tracker.WaitForWarmupAsync(linked.Token);
        
        await InsertSingleMessageAsync("warmup", linked.Token);

        await warmupTask; 
        Console.WriteLine("Consumer ready. Starting load test...");

        tracker.StartMeasurement();
        var sw = Stopwatch.StartNew();

        // High throughput inserts via connection pool
        await Parallel.ForEachAsync(Enumerable.Range(0, total), new ParallelOptions { MaxDegreeOfParallelism = 50, CancellationToken = linked.Token }, async (i, token) =>
        {
            var payloadId = Guid.NewGuid().ToString();
            await InsertSingleMessageAsync(payloadId, token);
        });

        long enqueueMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"Enqueued  : {total} messages into PostgreSQL outbox in {enqueueMs} ms — waiting for consumer...");

        await tracker.WaitForAllAsync(linked.Token);

        sw.Stop();
        long e2eMs        = sw.ElapsedMilliseconds;
        long processingMs = tracker.LastSignalMs - tracker.FirstSignalMs;

        Console.WriteLine();
        Console.WriteLine($"=== Results ===");
        Console.WriteLine($"Messages             : {total}");
        Console.WriteLine($"Outbox Insert        : {enqueueMs} ms  ({total / Math.Max(1.0, enqueueMs / 1000.0):F0} msg/sec)");
        Console.WriteLine($"Debezium+Kafka E2E   : {processingMs} ms  ({(processingMs > 0 ? total / (processingMs / 1000.0) : double.PositiveInfinity):F0} msg/sec)");
        Console.WriteLine($"Overall (post-warmup): {e2eMs} ms");
    }

    private async Task InsertSingleMessageAsync(string payloadId, CancellationToken ct)
    {
        var record = new SampleEvent
        {
            Id = payloadId,
            Message = $"Outbox Load Test Message"
        };

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
        await conn.OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(InsertOutboxSql, outboxMsg, cancellationToken: ct));
    }
}
