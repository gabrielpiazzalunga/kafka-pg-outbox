using System.Diagnostics;
using System.Threading.Channels;
using Dapper;
using NpgsqlTypes;
using Messaging.Contracts.Proto;
using FeedIngestion.Service.Infrastructure.Database;
using Messaging.Kafka.Outbox;

namespace FeedIngestion.Service;

public sealed class FeedIngestionJob(
    IDbConnectionFactory connectionFactory,
    OutboxSerializer outboxSerializer)
{
    private const string VisaTopic = "visa-settlements-protobuf.events";
    private const int BatchSize = 500;
    private const int ChannelCapacity = 10;
    private const int Parallelism = 10;
    private int _processedCount;

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
        Console.WriteLine($"[FeedIngestion] Starting ingestion for file: {fileId}");
        Console.WriteLine($"[FeedIngestion] Source path: {filePath}");
        Console.WriteLine($"[FeedIngestion] Config: BatchSize={BatchSize}, ChannelCapacity={ChannelCapacity}, Parallelism={Parallelism}");
        var sw = Stopwatch.StartNew();

        using (var conn = connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(UpsertFileSql, new { FileId = fileId }, cancellationToken: ct));
        }

        long startOffset = await GetRecoveryOffsetAsync(fileId, ct);
        if (startOffset > 0)
            Console.WriteLine($"[FeedIngestion] Resuming from record {startOffset}");

        var channel = Channel.CreateBounded<List<(long index, string line)>>(
            new BoundedChannelOptions(ChannelCapacity) { FullMode = BoundedChannelFullMode.Wait });

        var producerTask = ProduceBatchesAsync(filePath, startOffset, channel.Writer, ct);
        var consumerTasks = Enumerable.Range(0, Parallelism)
            .Select(_ => ConsumeAndProcessAsync(fileId, channel.Reader, ct))
            .ToArray();

        await Task.WhenAll(producerTask, Task.WhenAll(consumerTasks));

        using (var conn = connectionFactory.CreateConnection())
        {
            await conn.OpenAsync(ct);
            await conn.ExecuteAsync(new CommandDefinition(CompleteFileSql, new { FileId = fileId }, cancellationToken: ct));
        }

        sw.Stop();
        Console.WriteLine($"[FeedIngestion] Completed {_processedCount:N0} records in {sw.Elapsed.TotalSeconds:F1}s ({_processedCount / Math.Max(1, sw.Elapsed.TotalSeconds):F0} records/sec)");
    }

    private async Task<long> GetRecoveryOffsetAsync(string fileId, CancellationToken ct)
    {
        using var conn = connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);
        var cmd = new CommandDefinition(
            "SELECT COALESCE(MAX(record_index), 0) FROM file_offsets WHERE file_id = @FileId",
            new { FileId = fileId },
            cancellationToken: ct);
        return await conn.QuerySingleAsync<long>(cmd);
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

    private async Task ConsumeAndProcessAsync(
        string fileId,
        ChannelReader<List<(long index, string line)>> reader,
        CancellationToken ct)
    {
        await foreach (var batch in reader.ReadAllAsync(ct))
        {
            using var conn = connectionFactory.CreateConnection();
            await conn.OpenAsync(ct);
            using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                var phaseSw = Stopwatch.StartNew();

                // 1. Parse and serialize all records
                var parsedRecords = new List<(long index, VisaSettlementReceived evt, byte[] payload)>(batch.Count);
                foreach (var (index, line) in batch)
                {
                    var settlementEvent = ParseVisaLine(fileId, index, line);
                    byte[] protobufBytes = await outboxSerializer.SerializeProtobufAsync(settlementEvent, VisaTopic);
                    parsedRecords.Add((index, settlementEvent, protobufBytes));
                }
                long serializeMs = phaseSw.ElapsedMilliseconds;

                // 2. COPY binary import for file_offsets
                phaseSw.Restart();
                await using (var offsetWriter = await conn.BeginBinaryImportAsync(
                    "COPY file_offsets (file_id, record_index) FROM STDIN (FORMAT BINARY)", ct))
                {
                    foreach (var (index, _, _) in parsedRecords)
                    {
                        await offsetWriter.StartRowAsync(ct);
                        await offsetWriter.WriteAsync(fileId, NpgsqlDbType.Varchar, ct);
                        await offsetWriter.WriteAsync(index, NpgsqlDbType.Bigint, ct);
                    }
                    await offsetWriter.CompleteAsync(ct);
                }

                // 3. COPY binary import for outbox
                await using (var outboxWriter = await conn.BeginBinaryImportAsync(
                    "COPY outbox (id, aggregate_type, aggregate_id, type, payload, created_at) FROM STDIN (FORMAT BINARY)", ct))
                {
                    foreach (var (_, evt, payload) in parsedRecords)
                    {
                        await outboxWriter.StartRowAsync(ct);
                        await outboxWriter.WriteAsync(Guid.NewGuid(), NpgsqlDbType.Uuid, ct);
                        await outboxWriter.WriteAsync("visa-settlements", NpgsqlDbType.Varchar, ct);
                        await outboxWriter.WriteAsync(evt.AcquirerReferenceNumber, NpgsqlDbType.Varchar, ct);
                        await outboxWriter.WriteAsync("VisaSettlementReceived", NpgsqlDbType.Varchar, ct);
                        await outboxWriter.WriteAsync(payload, NpgsqlDbType.Bytea, ct);
                        await outboxWriter.WriteAsync(DateTime.UtcNow, NpgsqlDbType.TimestampTz, ct);
                    }
                    await outboxWriter.CompleteAsync(ct);
                }
                long copyMs = phaseSw.ElapsedMilliseconds;

                phaseSw.Restart();
                await tx.CommitAsync(ct);
                long commitMs = phaseSw.ElapsedMilliseconds;

                int current = Interlocked.Add(ref _processedCount, batch.Count);
                Console.WriteLine($"[FeedIngestion] Processed {current:N0} | Serialize={serializeMs}ms COPY={copyMs}ms Commit={commitMs}ms");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(ct);
                Console.WriteLine($"[FeedIngestion] Batch failed: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Mock parser — real implementation would extract fields from exact column positions.
    /// </summary>
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
