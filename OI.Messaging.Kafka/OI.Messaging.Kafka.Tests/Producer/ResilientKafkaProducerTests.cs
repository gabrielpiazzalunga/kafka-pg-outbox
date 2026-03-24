using Confluent.Kafka;
using NSubstitute;
using OI.Messaging.Kafka.Tests.Consumer;
using OI.Messaging.Kafka.Producer;
using OI.Messaging.Kafka.Resilience;
using Polly;
using Polly.Registry;
using Polly.Retry;
using Xunit;

namespace OI.Messaging.Kafka.Tests.Producer;

public class ResilientKafkaProducerTests
{
    private readonly IKafkaProducerClient _inner;
    private readonly ResilientKafkaProducer _sut;

    public ResilientKafkaProducerTests()
    {
        _inner = Substitute.For<IKafkaProducerClient>();

        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder(KafkaPipelines.Kafka, (builder, _) => { /* no-op: tests control behaviour via the mock */ });

        _sut = new ResilientKafkaProducer(_inner, registry);
    }

    // ── ProduceConfirmedAsync guard clauses ──────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProduceConfirmedAsync_WhenTopicIsNullOrWhitespace_ReturnsFail_WithoutCallingInner(string? topic)
    {
        var record = new FakeRecord();

        var result = await _sut.ProduceConfirmedAsync(record, topic!, new Headers());

        Assert.True(result.IsFailed);
        await _inner.DidNotReceive()
            .ProduceConfirmedAsync(Arg.Any<FakeRecord>(), Arg.Any<string>(), Arg.Any<Headers>(), Arg.Any<string?>());
    }

    // ── ProduceAsync guard clauses ──────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProduceAsync_WhenTopicIsNullOrWhitespace_ReturnsFail_WithoutCallingInner(string? topic)
    {
        var result = await _sut.ProduceAsync(Array.Empty<byte>(), topic!, new Headers());

        Assert.True(result.IsFailed);
        await _inner.DidNotReceive()
            .ProduceAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<Headers>());
    }

    // ── Delegation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ProduceConfirmedAsync_WhenTopicIsValid_DelegatesToInnerProducer()
    {
        var record = new FakeRecord();
        _inner.ProduceConfirmedAsync(record, "test-topic", Arg.Any<Headers>(), Arg.Any<string?>())
            .Returns(FluentResults.Result.Ok());

        var result = await _sut.ProduceConfirmedAsync(record, "test-topic", new Headers());

        Assert.True(result.IsSuccess);
        await _inner.Received(1).ProduceConfirmedAsync(record, "test-topic", Arg.Any<Headers>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ProduceAsync_WhenTopicIsValid_DelegatesToInnerProducer()
    {
        var payload = new byte[] { 1, 2, 3 };
        _inner.ProduceAsync(payload, "test-topic", Arg.Any<Headers>())
            .Returns(FluentResults.Result.Ok());

        var result = await _sut.ProduceAsync(payload, "test-topic", new Headers());

        Assert.True(result.IsSuccess);
        await _inner.Received(1).ProduceAsync(payload, "test-topic", Arg.Any<Headers>());
    }

    // ── Retry behaviour ─────────────────────────────────────────────────────

    [Fact]
    public async Task ProduceConfirmedAsync_WhenInnerThrowsTransiently_RetriesConfiguredTimes()
    {
        const int maxRetryAttempts = 2;
        var record = new FakeRecord();

        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder(KafkaPipelines.Kafka, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant
            }));

        var sut = new ResilientKafkaProducer(_inner, registry);

        _inner.ProduceConfirmedAsync(Arg.Any<FakeRecord>(), Arg.Any<string>(), Arg.Any<Headers>(), Arg.Any<string?>())
            .Returns(_ => Task.FromException<FluentResults.Result>(new InvalidOperationException("broker unavailable")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ProduceConfirmedAsync(record, "test-topic", new Headers()));

        // 1 initial attempt + maxRetryAttempts retries
        await _inner.Received(1 + maxRetryAttempts)
            .ProduceConfirmedAsync(Arg.Any<FakeRecord>(), Arg.Any<string>(), Arg.Any<Headers>(), Arg.Any<string?>());
    }

    [Fact]
    public async Task ProduceAsync_WhenInnerThrowsTransiently_RetriesConfiguredTimes()
    {
        const int maxRetryAttempts = 2;

        var registry = new ResiliencePipelineRegistry<string>();
        registry.TryAddBuilder(KafkaPipelines.Kafka, (builder, _) =>
            builder.AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxRetryAttempts,
                Delay = TimeSpan.Zero,
                BackoffType = DelayBackoffType.Constant
            }));

        var sut = new ResilientKafkaProducer(_inner, registry);

        _inner.ProduceAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<Headers>())
            .Returns(_ => Task.FromException<FluentResults.Result>(new InvalidOperationException("broker unavailable")));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ProduceAsync(Array.Empty<byte>(), "test-topic", new Headers()));

        await _inner.Received(1 + maxRetryAttempts)
            .ProduceAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<Headers>());
    }
}
