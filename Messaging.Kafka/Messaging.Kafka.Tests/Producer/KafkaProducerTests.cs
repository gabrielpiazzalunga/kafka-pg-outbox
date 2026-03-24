using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Messaging.Kafka.Options;
using Messaging.Kafka.Producer;
using Messaging.Kafka.Tests.Consumer;
using Xunit;

namespace Messaging.Kafka.Tests.Producer;

public class KafkaProducerTests
{
    private readonly KafkaProducer _sut;

    public KafkaProducerTests()
    {
        var connectionOptions = Substitute.For<IOptions<KafkaConnectionConfig>>();
        connectionOptions.Value.Returns(new KafkaConnectionConfig
        {
            BootstrapServer = "localhost:9092",
            SchemaRegistryUrl = "http://localhost:8081"
        });

        var producerOptions = Substitute.For<IOptions<KafkaProducerConfig>>();
        producerOptions.Value.Returns(new KafkaProducerConfig { ProduceTimeout = 5 });

        _sut = new KafkaProducer(
            connectionOptions,
            producerOptions,
            Substitute.For<ISchemaRegistryClient>(),
            Substitute.For<ILogger<KafkaProducer>>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProduceConfirmedAsync_WhenTopicIsNullOrWhitespace_ReturnsFailResult(string? topic)
    {
        var result = await _sut.ProduceConfirmedAsync(new FakeRecord(), topic!, new Headers());

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, e => e.Message == "No topic provided");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ProduceAsync_WhenTopicIsNullOrWhitespace_ReturnsFailResult(string? topic)
    {
        var result = await _sut.ProduceAsync(Array.Empty<byte>(), topic!, new Headers());

        Assert.True(result.IsFailed);
        Assert.Contains(result.Errors, e => e.Message == "No topic provided");
    }
}
