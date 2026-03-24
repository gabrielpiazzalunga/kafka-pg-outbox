using System.Diagnostics;
using Confluent.Kafka;
using OI.Messaging.Contracts.Proto;
using Timestamp = Google.Protobuf.WellKnownTypes.Timestamp;
using OI.Messaging.Kafka.Producer;

internal sealed class EventPublisher(
    IKafkaProducer producer,
    string mbsEventTopic,
    string mbsReadingTopic,
    string insReadingTopic)
{
    public async Task RunLoadTestAsync(int total, LoadTestConsumeTracker tracker, CancellationToken ct)
    {
        Console.WriteLine($"Load test: {total} messages → '{mbsEventTopic}'");

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        // Warmup: repeatedly produce until the consumer confirms receipt.
        // A single fire-and-forget isn't safe because the Kafka JoinGroup/rebalance
        // completes asynchronously AFTER host.StartAsync() returns. With AutoOffsetReset=Latest,
        // any message produced before partition assignment is silently skipped.
        Console.WriteLine("Sending warmup messages — waiting for consumer to be ready...");
        var warmupTask = tracker.WaitForWarmupAsync(linked.Token);
        while (true)
        {
            _ = producer.ProduceConfirmedAsync(
                new MBESReading { Envelope = new ReadingEnvelope { PayloadId = "warmup", Ts = Timestamp.FromDateTime(DateTime.UtcNow) } },
                mbsEventTopic, new Headers(), key: "warmup");
            if (await Task.WhenAny(warmupTask, Task.Delay(500, linked.Token)) == warmupTask)
                break;
        }
        await warmupTask; // propagate cancellation if it faulted
        Console.WriteLine("Consumer ready. Starting load test...");

        // Start timing only after the consumer is confirmed ready
        tracker.StartMeasurement();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < total; i++)
        {
            var payloadId = Guid.NewGuid().ToString();
            _ = producer.ProduceConfirmedAsync(
                new MBESReading { Envelope = new ReadingEnvelope { PayloadId = payloadId, Ts = Timestamp.FromDateTime(DateTime.UtcNow) } },
                mbsEventTopic, new Headers(), key: payloadId);
        }

        long enqueueMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"Enqueued  : {total} messages in {enqueueMs} ms — waiting for consumer...");

        await tracker.WaitForAllAsync(linked.Token);

        sw.Stop();
        long e2eMs        = sw.ElapsedMilliseconds;
        long processingMs = tracker.LastSignalMs - tracker.FirstSignalMs;

        Console.WriteLine();
        Console.WriteLine($"=== Results ===");
        Console.WriteLine($"Messages             : {total}");
        Console.WriteLine($"Enqueue              : {enqueueMs} ms  ({total / Math.Max(1.0, enqueueMs / 1000.0):F0} msg/sec)");
        Console.WriteLine($"Consumer throughput  : {processingMs} ms  ({(processingMs > 0 ? total / (processingMs / 1000.0) : double.PositiveInfinity):F0} msg/sec)");
        Console.WriteLine($"E2E (post-warmup)    : {e2eMs} ms");
    }

    public async Task RunNormalModeAsync(CancellationToken ct)
    {
        Console.WriteLine(
            $"Producing to '{mbsReadingTopic}', '{insReadingTopic}' every 5 s (Ctrl+C to exit)...");

        var rng = new Random();

        while (!ct.IsCancellationRequested)
        {
            var ts = Timestamp.FromDateTime(DateTime.UtcNow);
            var vessel = "vessel-01";

            var mbsReading = new MBESReading
            {
                Envelope = new ReadingEnvelope
                {
                    Ts = ts,
                    VesselId = vessel,
                    PayloadId = "mbes-001",
                    ConnectionStatus = ConnectionStatus.Connected,
                },
                DepthM       = 120.0 + rng.NextDouble() * 10,
                SwathWidthM  = 240.0 + rng.NextDouble() * 20,
                FrequencyKhz = 300,
                PingRateHz   = 10,
            };

            var insReading = new INSReading
            {
                Envelope = new ReadingEnvelope
                {
                    Ts = ts,
                    VesselId = vessel,
                    PayloadId = "ins-001",
                    ConnectionStatus = ConnectionStatus.Connected,
                },
                PosMode    = "Nav: Aligned",
                ImuStatus  = "OK",
                NavStatus  = "CA",
                GamsStatus = "OK",
                LatitudeDeg  = -33.8688 + rng.NextDouble() * 0.001,
                LongitudeDeg = 151.2093 + rng.NextDouble() * 0.001,
                HeadingDeg   = rng.NextDouble() * 360,
                SpeedKnots   = 5.0 + rng.NextDouble(),
            };

            var r1 = await producer.ProduceConfirmedAsync(mbsReading, mbsReadingTopic, new Headers(), key: mbsReading.Envelope.PayloadId);
            var r2 = await producer.ProduceConfirmedAsync(insReading,  insReadingTopic, new Headers(), key: insReading.Envelope.PayloadId);

            var ok = r1.IsSuccess && r2.IsSuccess;
            if (ok)
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] Produced MBESReading + INSReading");
            else
                Console.WriteLine($"[{DateTime.UtcNow:HH:mm:ss}] One or more produces failed");

            await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        }
    }
}
