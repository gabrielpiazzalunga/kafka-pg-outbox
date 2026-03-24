using Confluent.SchemaRegistry;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using static Microsoft.Extensions.Options.Options;
using OI.Messaging.Kafka.Common;
using OI.Messaging.Kafka.Consumer;
using OI.Messaging.Kafka.Options;
using OI.Messaging.Kafka.Producer;
using OI.Messaging.Kafka.Resilience;
using Polly;
using OI.Messaging.Kafka.Options.Resilience;

namespace OI.Messaging.Kafka;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOIKafkaProducer(
        this IServiceCollection services,
        KafkaConnectionConfig connection,
        KafkaProducerConfig producerConfig,
        KafkaResilienceOptions resilience)
        => services.AddOIKafkaProducer(_ => connection, _ => producerConfig, resilience);

    /// <summary>
    /// Registers a Kafka producer whose connection and producer configs are resolved lazily
    /// from the <see cref="IServiceProvider"/> at DI build time.  Use this overload when
    /// the configs are sourced from <c>IConfiguration</c> so that test overrides applied
    /// via <c>WebApplicationFactory.ConfigureWebHost</c> are picked up correctly.
    /// </summary>
    public static IServiceCollection AddOIKafkaProducer(
        this IServiceCollection services,
        Func<IServiceProvider, KafkaConnectionConfig> connectionFactory,
        Func<IServiceProvider, KafkaProducerConfig> producerConfigFactory,
        KafkaResilienceOptions resilience)
    {
        services.TryAddSingleton<IOptions<KafkaConnectionConfig>>(sp =>
            Create(connectionFactory(sp)));

        // Singleton ISchemaRegistryClient — TryAdd so producer and consumer can coexist
        services.TryAddSingleton<ISchemaRegistryClient>(sp =>
            new CachedSchemaRegistryClient(
                KafkaUtility.BuildSchemaRegistryConfig(
                    sp.GetRequiredService<IOptions<KafkaConnectionConfig>>().Value)));

        services.AddSingleton<IOptions<KafkaProducerConfig>>(sp =>
            Create(producerConfigFactory(sp)));

        // Resilience pipeline
        services.CreateKafkaResiliencePipeline(resilience);

        // Inner producer registered behind the internal interface for testability
        services.AddSingleton<IKafkaProducerClient, KafkaProducer>();

        // Public-facing interface resolved as the resilient decorator
        services.AddSingleton<IKafkaProducer, ResilientKafkaProducer>();

        return services;
    }

    public static IServiceCollection AddOIKafkaConsumer<TMessage, THandler>(
        this IServiceCollection services,
        KafkaConnectionConfig connection,
        KafkaConsumerConfig consumerConfig)
        where TMessage : class, IMessage<TMessage>, new()
        where THandler : class, IMessageHandler<TMessage>
        => services.AddOIKafkaConsumer<TMessage, THandler>(_ => connection, _ => consumerConfig);

    /// <summary>
    /// Registers a Kafka consumer whose connection and consumer configs are resolved lazily
    /// from the <see cref="IServiceProvider"/> at DI build time.  Use this overload when
    /// the configs are sourced from <c>IConfiguration</c> so that test overrides applied
    /// via <c>WebApplicationFactory.ConfigureWebHost</c> are picked up correctly.
    /// </summary>
    public static IServiceCollection AddOIKafkaConsumer<TMessage, THandler>(
        this IServiceCollection services,
        Func<IServiceProvider, KafkaConnectionConfig> connectionFactory,
        Func<IServiceProvider, KafkaConsumerConfig> consumerConfigFactory)
        where TMessage : class, IMessage<TMessage>, new()
        where THandler : class, IMessageHandler<TMessage>
    {
        services.TryAddSingleton<IOptions<KafkaConnectionConfig>>(sp =>
            Create(connectionFactory(sp)));

        // TryAdd so the schema registry client is shared if the producer is also registered
        services.TryAddSingleton<ISchemaRegistryClient>(sp =>
            new CachedSchemaRegistryClient(
                KafkaUtility.BuildSchemaRegistryConfig(
                    sp.GetRequiredService<IOptions<KafkaConnectionConfig>>().Value)));

        services.AddSingleton<IMessageHandler<TMessage>, THandler>();
        services.AddSingleton<IKafkaConsumerFactory<TMessage>>(sp =>
            new KafkaConsumerFactory<TMessage>(
                sp.GetRequiredService<IOptions<KafkaConnectionConfig>>(),
                consumerConfigFactory(sp)));
        services.AddHostedService<KafkaConsumerWorker<TMessage>>();

        return services;
    }

    public static void CreateKafkaResiliencePipeline(this IServiceCollection serviceCollection, KafkaResilienceOptions resilienceOptions)
    {
        serviceCollection.AddResiliencePipeline(KafkaPipelines.Kafka, builder =>
        {
            builder.AddTimeout(KafkaStrategies.GetTimeoutConfiguration(resilienceOptions.Timeout!));
            builder.AddRetry(KafkaStrategies.GetRetryConfiguration(resilienceOptions.Retry!));
            builder.AddCircuitBreaker(KafkaStrategies.GetCircuitBreakerConfiguration(resilienceOptions.CircuitBreaker!));
        });
    }
}
