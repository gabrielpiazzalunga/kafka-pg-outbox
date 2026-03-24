using System.Threading.Channels;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using OI.Messaging.Kafka.Consumer;
using OI.Messaging.Kafka.Producer;

namespace OI.Messaging.Kafka.Client.Integration.Tests;

[Collection("Kafka Integration")]
public sealed class ProducerConsumerIntegrationTests(KafkaIntegrationFixture fixture)
{
    // -----------------------------------------------------------------------
    // Tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ProduceAndConsume_RoundTrip_MessageArrivesWithCorrectKeyAndValue()
    {
        var topic = UniqueTopic();
        using var host = fixture.BuildHost<CaptureHandler>(topic, UniqueGroupId());
        await host.StartAsync();

        var producer = host.Services.GetRequiredService<IKafkaProducer>();
        var handler = (CaptureHandler)host.Services.GetRequiredService<IMessageHandler<TestEvent>>();
        var evt = new TestEvent { Id = Guid.NewGuid().ToString(), Payload = "hello-integration" };

        await producer.ProduceConfirmedAsync(evt, topic, new Headers(), key: evt.Id);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (key, value) = await handler.Messages.ReadAsync(cts.Token);

        Assert.Equal(evt.Id, key);
        Assert.Equal(evt.Id, value.Id);
        Assert.Equal(evt.Payload, value.Payload);

        await host.StopAsync();
    }

    [Fact]
    public async Task Consumer_WhenSameGroupId_DoesNotRedeliverAfterRestart()
    {
        var topic = UniqueTopic();
        var groupId = UniqueGroupId();

        // Round 1 — consume the message and let the worker commit the offset on Close()
        using var host1 = fixture.BuildHost<CaptureHandler>(topic, groupId);
        await host1.StartAsync();

        var producer = host1.Services.GetRequiredService<IKafkaProducer>();
        var handler1 = (CaptureHandler)host1.Services.GetRequiredService<IMessageHandler<TestEvent>>();
        var evt = new TestEvent { Id = Guid.NewGuid().ToString(), Payload = "no-redeliver" };

        await producer.ProduceConfirmedAsync(evt, topic, new Headers(), key: evt.Id);

        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await handler1.Messages.ReadAsync(cts1.Token); // blocks until the message is handled

        // StopAsync triggers cancellation → ExecuteAsync exits → consumer.Close() commits stored offset
        await host1.StopAsync();

        // Round 2 — same group-id, should NOT redeliver
        using var host2 = fixture.BuildHost<CaptureHandler>(topic, groupId);
        await host2.StartAsync();
        var handler2 = (CaptureHandler)host2.Services.GetRequiredService<IMessageHandler<TestEvent>>();

        using var cts2 = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ex = await Record.ExceptionAsync(() => handler2.Messages.ReadAsync(cts2.Token).AsTask());
        Assert.IsType<OperationCanceledException>(ex);

        await host2.StopAsync();
    }

    [Fact]
    public async Task Consumer_WithTopicPattern_ReceivesFromMultipleMatchingTopics()
    {
        var uid = Guid.NewGuid().ToString("N")[..8];
        var topicA = $"localdev.pattern-{uid}-a";
        var topicB = $"localdev.pattern-{uid}-b";
        var pattern = $"^localdev\\.pattern-{uid}-.*";

        await fixture.CreateTopicsAsync(topicA, topicB);

        using var host = fixture.BuildHost<CaptureHandler>(pattern, UniqueGroupId());
        await host.StartAsync();

        var producer = host.Services.GetRequiredService<IKafkaProducer>();
        var handler = (CaptureHandler)host.Services.GetRequiredService<IMessageHandler<TestEvent>>();

        var evtA = new TestEvent { Id = "a", Payload = "from-A" };
        var evtB = new TestEvent { Id = "b", Payload = "from-B" };

        await producer.ProduceConfirmedAsync(evtA, topicA, new Headers(), key: evtA.Id);
        await producer.ProduceConfirmedAsync(evtB, topicB, new Headers(), key: evtB.Id);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var first = await handler.Messages.ReadAsync(cts.Token);
        var second = await handler.Messages.ReadAsync(cts.Token);

        var payloads = new[] { first.Value.Payload, second.Value.Payload };
        Assert.Contains("from-A", payloads);
        Assert.Contains("from-B", payloads);

        await host.StopAsync();
    }

    [Fact]
    public async Task Consumer_WithConcurrentMessageLimit_LimitsConcurrentHandlerExecutions()
    {
        const int limit = 3;
        const int messageCount = 9; // 3× the limit so the peak is clearly observable

        var topic = UniqueTopic();

        using var host = fixture.BuildHost<ConcurrencyTrackingHandler>(topic, UniqueGroupId(), concurrentMessageLimit: limit);
        await host.StartAsync();

        var producer = host.Services.GetRequiredService<IKafkaProducer>();
        var handler = (ConcurrencyTrackingHandler)host.Services.GetRequiredService<IMessageHandler<TestEvent>>();

        for (int i = 0; i < messageCount; i++)
        {
            var evt = new TestEvent { Id = i.ToString(), Payload = "concurrency-test" };
            await producer.ProduceConfirmedAsync(evt, topic, new Headers(), key: evt.Id);
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for (int i = 0; i < messageCount; i++)
            await handler.Completions.ReadAsync(cts.Token);

        Assert.True(handler.PeakConcurrency > 1,
            $"Expected concurrent handler execution, but peak was {handler.PeakConcurrency}");
        Assert.True(handler.PeakConcurrency <= limit,
            $"Expected peak ≤ {limit}, but got {handler.PeakConcurrency}");

        await host.StopAsync();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string UniqueTopic() => $"localdev.it-{Guid.NewGuid():N}";
    private static string UniqueGroupId() => $"it-group-{Guid.NewGuid():N}";
}

// ---------------------------------------------------------------------------
// Capture handler — collects received messages via an unbounded channel
// ---------------------------------------------------------------------------

internal sealed class CaptureHandler : IMessageHandler<TestEvent>
{
    private readonly Channel<(string? Key, TestEvent Value)> _channel =
        Channel.CreateUnbounded<(string?, TestEvent)>();

    public ChannelReader<(string? Key, TestEvent Value)> Messages => _channel.Reader;

    public Task HandleAsync(string? key, TestEvent @event, string topic,
        Offset offset, Partition partition, Headers headers)
    {
        _channel.Writer.TryWrite((key, @event));
        return Task.CompletedTask;
    }
}

// ---------------------------------------------------------------------------
// Concurrency-tracking handler — measures peak simultaneous HandleAsync calls
// ---------------------------------------------------------------------------

internal sealed class ConcurrencyTrackingHandler : IMessageHandler<TestEvent>
{
    private int _active;
    private int _peak;
    private readonly Channel<int> _completions = Channel.CreateUnbounded<int>();

    public ChannelReader<int> Completions => _completions.Reader;
    public int PeakConcurrency => Volatile.Read(ref _peak);

    public async Task HandleAsync(string? key, TestEvent @event, string topic,
        Offset offset, Partition partition, Headers headers)
    {
        var active = Interlocked.Increment(ref _active);
        UpdateMax(ref _peak, active);

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Interlocked.Decrement(ref _active);
        _completions.Writer.TryWrite(active);
    }

    private static void UpdateMax(ref int location, int candidate)
    {
        int current;
        do { current = location; }
        while (candidate > current && Interlocked.CompareExchange(ref location, candidate, current) != current);
    }
}
