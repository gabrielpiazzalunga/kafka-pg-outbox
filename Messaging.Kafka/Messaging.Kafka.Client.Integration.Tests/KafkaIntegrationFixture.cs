using Confluent.Kafka;
using Confluent.Kafka.Admin;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Messaging.Kafka;
using Messaging.Kafka.Client.Integration.Tests.SchemaRegistry;
using Messaging.Kafka.Consumer;
using Messaging.Kafka.Options;
using Messaging.Kafka.Options.Resilience;
using OI.Messaging.Kafka.Client.Integration.Tests;
using Testcontainers.Kafka;

namespace Messaging.Kafka.Client.Integration.Tests;

[CollectionDefinition("Kafka Integration")]
public class KafkaIntegrationCollection : ICollectionFixture<KafkaIntegrationFixture> { }

public sealed class KafkaIntegrationFixture : IAsyncLifetime
{
    private static readonly INetwork s_network = new NetworkBuilder()
        .WithName(Guid.NewGuid().ToString())
        .Build();

    // Pin to 7.5.12 — supports Zookeeper mode and is the official Testcontainers.Kafka default.
    // SchemaRegistry connects to Kafka inside the network via the "kafka" alias on BrokerPort 9093.
    public readonly KafkaContainer KafkaContainer = new KafkaBuilder("confluentinc/cp-kafka:7.5.12")
        .WithNetwork(s_network)
        .WithNetworkAliases("kafka")
        .Build();

    public readonly SchemaRegistryContainer SchemaRegistryContainer = new SchemaRegistryBuilder()
        .WithNetwork(s_network)
        .Build();

    /// <summary>
    /// Confluent-style bootstrap address: <c>host:port</c> (no scheme prefix).
    /// </summary>
    public string BootstrapServer { get; private set; } = string.Empty;

    /// <summary>
    /// Full HTTP URL for the Schema Registry, e.g. <c>http://localhost:52345</c>.
    /// </summary>
    public string SchemaRegistryUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await s_network.CreateAsync();
        await KafkaContainer.StartAsync();
        await SchemaRegistryContainer.StartAsync();

        BootstrapServer = $"{KafkaContainer.Hostname}:{KafkaContainer.GetMappedPublicPort(KafkaBuilder.KafkaPort)}";
        SchemaRegistryUrl = $"http://{SchemaRegistryContainer.GetBootstrapAddress()}";
    }

    public IHost BuildHost<THandler>(
        string topicPattern, string groupId,
        int? concurrentMessageLimit = null)
        where THandler : class, IMessageHandler<TestEvent>
    {
        var connection = new KafkaConnectionConfig
        {
            BootstrapServer = BootstrapServer,
            SchemaRegistryUrl = SchemaRegistryUrl,
        };

        var producerConfig = new KafkaProducerConfig { ClientName = "it-producer", ProduceTimeout = 30 };

        var resilience = new KafkaResilienceOptions
        {
            Timeout = new KafkaResilienceTimeoutOptions { Timeout = 60 },
            Retry = new KafkaResilienceRetryOptions { MaxRetryAttempts = 1, Delay = 1 },
            CircuitBreaker = new KafkaResilienceCircuitBreakerOptions
            {
                SamplingDuration = 60,
                FailureRatio = 0.99,
                MinimumThroughput = 1000,
                BreakDuration = 5,
            },
        };

        var consumerConfig = new KafkaConsumerConfig
        {
            TopicPattern = topicPattern,
            ConsumerGroupId = groupId,
            AutoOffsetReset = "Earliest",
            ConcurrentMessageLimit = concurrentMessageLimit,
        };

        return new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));
                services.AddKafkaProducer(connection, producerConfig, resilience);
                services.AddKafkaConsumer<TestEvent, THandler>(connection, consumerConfig);
            })
            .Build();
    }

    public async Task CreateTopicsAsync(params string[] topics)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServer }).Build();
        await admin.CreateTopicsAsync(topics.Select(t => new TopicSpecification
        {
            Name = t,
            NumPartitions = 1,
            ReplicationFactor = 1,
        }));
    }

    public async Task DisposeAsync()
    {
        await KafkaContainer.DisposeAsync();
        await SchemaRegistryContainer.DisposeAsync();
        await s_network.DisposeAsync();
    }
}
