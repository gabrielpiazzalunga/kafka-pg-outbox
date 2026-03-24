using System.Diagnostics;
using Confluent.Kafka;
using Messaging.Contracts.Proto;
using Messaging.Kafka.Consumer;
using Messaging.Contracts.Proto;

/// <summary>
/// Counts down consumed messages so the load test can wait for full end-to-end completion.
///
/// The first call to <see cref="Signal"/> is treated as a warmup signal: it completes
/// <see cref="WaitForWarmupAsync"/> but does not decrement the real-message counter.
/// Call <see cref="StartMeasurement"/> after warmup to begin timing.
/// </summary>
internal sealed class LoadTestConsumeTracker
{
    private int _remaining;
    private readonly TaskCompletionSource _warmupTcs    = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completionTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _isWarmedUp;        // CAS flag: 0 = not yet, 1 = done
    private readonly Stopwatch _sw = new();

    private long _firstSignalMs = -1;
    private long _lastSignalMs;

    public LoadTestConsumeTracker(int total) => _remaining = total;

    public long FirstSignalMs => Volatile.Read(ref _firstSignalMs);
    public long LastSignalMs  => Volatile.Read(ref _lastSignalMs);

    /// <summary>Starts (or restarts) the internal clock used for <see cref="FirstSignalMs"/> / <see cref="LastSignalMs"/>.</summary>
    public void StartMeasurement() => _sw.Restart();

    /// <summary>Completes when the warmup message has been consumed.</summary>
    public Task WaitForWarmupAsync(CancellationToken ct = default) =>
        _warmupTcs.Task.WaitAsync(ct);

    /// <summary>Completes when all <c>total</c> real messages have been consumed.</summary>
    public Task WaitForAllAsync(CancellationToken ct = default) =>
        _completionTcs.Task.WaitAsync(ct);

    public void Signal()
    {
        // First signal = warmup probe; don't count toward _remaining
        if (Interlocked.CompareExchange(ref _isWarmedUp, 1, 0) == 0)
        {
            _warmupTcs.TrySetResult();
            return;
        }

        var now = _sw.ElapsedMilliseconds;
        Interlocked.CompareExchange(ref _firstSignalMs, now, -1);
        Volatile.Write(ref _lastSignalMs, now);

        if (Interlocked.Decrement(ref _remaining) == 0)
            _completionTcs.TrySetResult();
    }
}

/// <summary>
/// Minimal handler used during load tests — signals the tracker for every consumed message.
/// </summary>
internal sealed class LoadTestHandler(LoadTestConsumeTracker tracker) : IMessageHandler<SampleEvent>
{
    public Task HandleAsync(string? key, SampleEvent message, string topic,
        Offset offset, Partition partition, Headers headers)
    {
        tracker.Signal();
        return Task.CompletedTask;
    }
}
