using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Messaging.Kafka.Consumer;
using Messaging.Kafka.Options;
using Xunit;

namespace Messaging.Kafka.Tests.Consumer;

public class KafkaConsumerWorkerTests
{
    private readonly IConsumer<string?, FakeRecord> _consumer;
    private readonly IMessageHandler<FakeRecord> _handler;
    private readonly ILogger<KafkaConsumerWorker<FakeRecord>> _logger;

    public KafkaConsumerWorkerTests()
    {
        _consumer = Substitute.For<IConsumer<string?, FakeRecord>>();
        _handler = Substitute.For<IMessageHandler<FakeRecord>>();
        _logger = Substitute.For<ILogger<KafkaConsumerWorker<FakeRecord>>>();
    }

    private KafkaConsumerWorker<FakeRecord> BuildSut(string? topicPattern = "test.*", int? concurrentLimit = null)
    {
        var factory = Substitute.For<IKafkaConsumerFactory<FakeRecord>>();
        factory.Create().Returns(_consumer);
        factory.Config.Returns(new KafkaConsumerConfig { TopicPattern = topicPattern, ConcurrentMessageLimit = concurrentLimit });

        return new KafkaConsumerWorker<FakeRecord>(factory, _handler, _logger);
    }

    private static ConsumeResult<string?, FakeRecord> MakeResult(
        FakeRecord? value = null,
        string topic = "test.topic",
        int partition = 0,
        long offset = 10,
        string? key = "msg-key",
        bool isEof = false)
    {
        return new ConsumeResult<string?, FakeRecord>
        {
            Topic = topic,
            Partition = new Partition(partition),
            Offset = new Offset(offset),
            IsPartitionEOF = isEof,
            Message = isEof ? null! : new Message<string?, FakeRecord>
            {
                Key = key,
                Value = value ?? new FakeRecord(),
                Headers = new Headers()
            }
        };
    }

    /// <summary>
    /// Configures the consumer mock to return <paramref name="result"/> on the first call,
    /// then block until the cancellation token fires on subsequent calls.
    /// </summary>
    private void SetupConsumerOneResult(ConsumeResult<string?, FakeRecord> result)
    {
        int calls = 0;
        _consumer.Consume(Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var ct = ci.Arg<CancellationToken>();
            if (Interlocked.Increment(ref calls) == 1) return result;
            ct.WaitHandle.WaitOne();
            ct.ThrowIfCancellationRequested();
            return default!;
        });
    }

    // ── Guard clause ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ExecuteAsync_WhenTopicPatternIsNullOrWhitespace_ThrowsInvalidOperationException(string? pattern)
    {
        var sut = BuildSut(topicPattern: pattern);

        await ((IHostedService)sut).StartAsync(CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExecuteTask!);
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenValidMessage_CallsHandlerWithCorrectArguments()
    {
        var record = new FakeRecord();
        var cr = MakeResult(record, topic: "test.topic", partition: 2, offset: 42, key: "my-key");
        SetupConsumerOneResult(cr);

        var handlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.HandleAsync(
            Arg.Any<string?>(), Arg.Any<FakeRecord>(), Arg.Any<string>(),
            Arg.Any<Offset>(), Arg.Any<Partition>(), Arg.Any<Headers>())
            .Returns(_ => { handlerCalled.TrySetResult(); return Task.CompletedTask; });

        var sut = BuildSut();
        var hs = (IHostedService)sut;

        await hs.StartAsync(CancellationToken.None);
        await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hs.StopAsync(CancellationToken.None);

        await _handler.Received(1).HandleAsync(
            "my-key", record, "test.topic",
            new Offset(42), new Partition(2), Arg.Any<Headers>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidMessage_StoresOffset()
    {
        var cr = MakeResult();
        SetupConsumerOneResult(cr);

        var offsetStored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumer.When(c => c.StoreOffset(Arg.Any<ConsumeResult<string?, FakeRecord>>()))
            .Do(_ => offsetStored.TrySetResult());

        var sut = BuildSut();
        var hs = (IHostedService)sut;

        await hs.StartAsync(CancellationToken.None);
        await offsetStored.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hs.StopAsync(CancellationToken.None);

        _consumer.Received(1).StoreOffset(cr);
    }

    // ── Partition EOF ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenPartitionEOF_DoesNotCallHandler()
    {
        int calls = 0;
        var loopedBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumer.Consume(Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var ct = ci.Arg<CancellationToken>();
            if (Interlocked.Increment(ref calls) == 1) return MakeResult(isEof: true);
            loopedBack.TrySetResult();
            ct.WaitHandle.WaitOne();
            ct.ThrowIfCancellationRequested();
            return default!;
        });

        var sut = BuildSut();
        var hs = (IHostedService)sut;

        await hs.StartAsync(CancellationToken.None);
        await loopedBack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hs.StopAsync(CancellationToken.None);

        await _handler.DidNotReceive().HandleAsync(
            Arg.Any<string?>(), Arg.Any<FakeRecord>(), Arg.Any<string>(),
            Arg.Any<Offset>(), Arg.Any<Partition>(), Arg.Any<Headers>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenPartitionEOF_DoesNotStoreOffset()
    {
        int calls = 0;
        var loopedBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumer.Consume(Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var ct = ci.Arg<CancellationToken>();
            if (Interlocked.Increment(ref calls) == 1) return MakeResult(isEof: true);
            loopedBack.TrySetResult();
            ct.WaitHandle.WaitOne();
            ct.ThrowIfCancellationRequested();
            return default!;
        });

        var sut = BuildSut();
        var hs = (IHostedService)sut;

        await hs.StartAsync(CancellationToken.None);
        await loopedBack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hs.StopAsync(CancellationToken.None);

        _consumer.DidNotReceive().StoreOffset(Arg.Any<ConsumeResult<string?, FakeRecord>>());
    }

    // ── Exception handling ────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenConsumeExceptionThrown_DoesNotRethrowAndDoesNotCallHandler()
    {
        int calls = 0;
        var loopedBack = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumer.Consume(Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var ct = ci.Arg<CancellationToken>();
            if (Interlocked.Increment(ref calls) == 1)
                throw new ConsumeException(
                    new ConsumeResult<byte[], byte[]>(),
                    new Error(ErrorCode.BrokerNotAvailable, "broker down"));
            loopedBack.TrySetResult();
            ct.WaitHandle.WaitOne();
            ct.ThrowIfCancellationRequested();
            return default!;
        });

        var sut = BuildSut();
        var hs = (IHostedService)sut;

        await hs.StartAsync(CancellationToken.None);
        await loopedBack.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hs.StopAsync(CancellationToken.None);

        await _handler.DidNotReceive().HandleAsync(
            Arg.Any<string?>(), Arg.Any<FakeRecord>(), Arg.Any<string>(),
            Arg.Any<Offset>(), Arg.Any<Partition>(), Arg.Any<Headers>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerThrows_DoesNotRethrow()
    {
        var cr = MakeResult();
        SetupConsumerOneResult(cr);

        var handlerCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.HandleAsync(
            Arg.Any<string?>(), Arg.Any<FakeRecord>(), Arg.Any<string>(),
            Arg.Any<Offset>(), Arg.Any<Partition>(), Arg.Any<Headers>())
            .Returns(_ =>
            {
                handlerCalled.TrySetResult();
                return Task.FromException(new InvalidOperationException("handler boom"));
            });

        var sut = BuildSut();
        var hs = (IHostedService)sut;

        await hs.StartAsync(CancellationToken.None);
        await handlerCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hs.StopAsync(CancellationToken.None);

        // StopAsync returning without exception is the verification
    }

    [Fact]
    public async Task ExecuteAsync_WhenHandlerThrows_StillStoresOffset()
    {
        var cr = MakeResult();
        SetupConsumerOneResult(cr);

        var offsetStored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _consumer.When(c => c.StoreOffset(Arg.Any<ConsumeResult<string?, FakeRecord>>()))
            .Do(_ => offsetStored.TrySetResult());

        _handler.HandleAsync(
            Arg.Any<string?>(), Arg.Any<FakeRecord>(), Arg.Any<string>(),
            Arg.Any<Offset>(), Arg.Any<Partition>(), Arg.Any<Headers>())
            .Returns(_ => Task.FromException(new InvalidOperationException("handler boom")));

        var sut = BuildSut();
        var hs = (IHostedService)sut;

        await hs.StartAsync(CancellationToken.None);
        await offsetStored.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await hs.StopAsync(CancellationToken.None);

        _consumer.Received(1).StoreOffset(cr);
    }
}
