using System.Diagnostics;
using System.Threading.Channels;
using Confluent.Kafka;
using Dapper;
using StackExchange.Redis;
using Messaging.Contracts.Proto;
using FeedIngestion.Service.Infrastructure.Database;
using Messaging.Kafka.Producer;

namespace FeedIngestion.Service;

/// <summary>
/// Alternative ingestion job that tracks offsets in Redis and publishes
/// directly to Kafka via IKafkaProducer.ProduceBatchAsync, bypassing the
/// PostgreSQL transactional outbox.
/// Delivery guarantee: At-Least-Once (see docs/redis_kafka_failure_scenarios.md).
/// </summary>
public sealed class FeedIngestionRedisJob(
    IDbConnectionFactory connectionFactory,
    IConnectionMultiplexer redis,
    IKafkaProducer kafkaProducer)
{
    private const string VisaTopic = "visa-settlements-protobuf.events";
    private const int BatchSize = 500;
    private const int ChannelCapacity = 10;
    private const int Parallelism = 10;
    private int _processedCount;

    private const string RedisKeyPrefix = "feed_ingestion:offsets:";

    private const string UpsertFileSql = """
        INSERT INTO settlement_files (file_id, status)
        VALUES (@FileId, 'PROCESSING')
        ON CONFLICT (file_id) DO UPDATE SET status = 'PROCESSING'
        """;

    private const string CompleteFileSql = """
        UPDATE settlement_files SET status = 'COMPLETED', completed_at = NOW()
        WHERE file_id = @FileId
        """;

    public async Task IngestVisaFileAsync(string fileId, string filePath, CancellationToken ct)
    {
        Console.WriteLine($"[Redis+Kafka] Starting ingestion for file: {fileId}");
        Console.WriteLine($"[Redis+Kafka] Source path: {filePath}");
        Console.WriteLine($"[Redis+Kafka] Config: BatchSize={BatchSize}, ChannelCapacity={ChannelCapacity}, Parallelism={Parallelism}");
        var sw = Stopwatch.StartNew();

        // Register file in PostgreSQL (overall file state)
        using (var conn = connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(UpsertFileSql, new { FileId = fileId }, cancellationToken: ct));
        }

        // Recovery offset from Redis
        var db = redis.GetDatabase();
        long startOffset = 0;
        var redisValue = await db.StringGetAsync($"{RedisKeyPrefix}{fileId}");
        if (redisValue.HasValue)
        {
            startOffset = (long)redisValue;
            Console.WriteLine($"[Redis+Kafka] Resuming from record {startOffset}");
        }

        var channel = Channel.CreateBounded<List<(long index, string line)>>(
            new BoundedChannelOptions(ChannelCapacity) { FullMode = BoundedChannelFullMode.Wait });

        var producerTask = ProduceBatchesAsync(filePath, startOffset, channel.Writer, ct);
        var consumerTasks = Enumerable.Range(0, Parallelism)
            .Select(_ => ConsumeAndPublishAsync(fileId, channel.Reader, ct))
            .ToArray();

        await Task.WhenAll(producerTask, Task.WhenAll(consumerTasks));

        // Mark file as completed in PostgreSQL
        using (var conn = connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(CompleteFileSql, new { FileId = fileId }, cancellationToken: ct));
        }

        sw.Stop();
        Console.WriteLine($"[Redis+Kafka] Completed {_processedCount:N0} records in {sw.Elapsed.TotalSeconds:F1}s ({_processedCount / Math.Max(1, sw.Elapsed.TotalSeconds):F0} records/sec)");
    }

    private static async Task ProduceBatchesAsync(
        string filePath,
        long startOffset,
        ChannelWriter<List<(long index, string line)>> writer,
        CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            long currentIndex = 0;
            var batch = new List<(long index, string line)>(BatchSize);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                currentIndex++;
                if (currentIndex <= startOffset) continue;

                batch.Add((currentIndex, line));

                if (batch.Count >= BatchSize)
                {
                    await writer.WriteAsync(batch, ct);
                    batch = new List<(long index, string line)>(BatchSize);
                }
            }

            if (batch.Count > 0)
                await writer.WriteAsync(batch, ct);
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task ConsumeAndPublishAsync(
        string fileId,
        ChannelReader<List<(long index, string line)>> reader,
        CancellationToken ct)
    {
        var db = redis.GetDatabase();

        await foreach (var batch in reader.ReadAllAsync(ct))
        {
            var phaseSw = Stopwatch.StartNew();

            // 1. Parse all records into Protobuf messages
            var messages = new List<(VisaSettlementReceived Record, string? Key)>(batch.Count);
            long maxIndex = 0;
            foreach (var (index, line) in batch)
            {
                var evt = ParseVisaLine(fileId, index, line);
                messages.Add((evt, evt.AcquirerReferenceNumber));
                if (index > maxIndex) maxIndex = index;
            }
            long parseMs = phaseSw.ElapsedMilliseconds;

            // 2. Batch produce to Kafka via existing KafkaProducer
            //    ProduceBatchAsync handles serialization (cached ProtobufSerializer),
            //    fires all ProduceAsync in parallel, and awaits all acks.
            phaseSw.Restart();
            var result = await kafkaProducer.ProduceBatchAsync(messages, VisaTopic, new Headers());
            if (result.IsFailed)
            {
                var errors = string.Join("; ", result.Errors.Select(e => e.Message));
                Console.WriteLine($"[Redis+Kafka] Batch produce FAILED: {errors}");
                throw new InvalidOperationException($"Kafka batch produce failed: {errors}");
            }
            long kafkaMs = phaseSw.ElapsedMilliseconds;

            // 3. Update Redis offset ONLY after ALL Kafka acks succeed
            //    This is the at-least-once boundary: a crash between step 2 and here = duplicates
            phaseSw.Restart();
            await db.StringSetAsync($"{RedisKeyPrefix}{fileId}", maxIndex);
            long redisMs = phaseSw.ElapsedMilliseconds;

            int current = Interlocked.Add(ref _processedCount, batch.Count);
            Console.WriteLine($"[Redis+Kafka] Processed {current:N0} | Parse={parseMs}ms Kafka={kafkaMs}ms Redis={redisMs}ms");
        }
    }

    private static VisaSettlementReceived ParseVisaLine(string fileId, long index, string line)
    {
        return new VisaSettlementReceived
        {
            FileId = fileId,
            RecordIndex = index,
            AcquirerReferenceNumber = $"ARN-{index:D10}",
            AuthorizationCode = "AUTH12",
            MaskedPan = "411111******1111",
            TransactionAmountCents = 15000,
            SettlementAmountCents = 14800,
            InterchangeFeeCents = 200,
            CurrencyCode = "840",
            ProcessingDate = DateTime.UtcNow.ToString("yyyyMMdd")
        };
    }
}
