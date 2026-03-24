using Docker.DotNet.Models;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Testcontainers.Kafka;

namespace Messaging.Kafka.Client.Integration.Tests.SchemaRegistry;

/// <inheritdoc cref="ContainerBuilder{TBuilderEntity, TContainerEntity, TConfigurationEntity}" />
public sealed class SchemaRegistryBuilder : ContainerBuilder<SchemaRegistryBuilder, SchemaRegistryContainer, SchemaRegistryConfiguration>
{
    public const string SchemaRegistryImage = "confluentinc/cp-schema-registry:7.5.12";

    public const ushort SchemaRegistryPort = 8081;
    public string KafkaUrl = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaBuilder" /> class.
    /// </summary>
    public SchemaRegistryBuilder()
        : this(new SchemaRegistryConfiguration())
    {
        DockerResourceConfiguration = Init().DockerResourceConfiguration;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaBuilder" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    private SchemaRegistryBuilder(SchemaRegistryConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        DockerResourceConfiguration = resourceConfiguration;
    }

    /// <inheritdoc />
    protected override SchemaRegistryConfiguration DockerResourceConfiguration { get; }

    /// <inheritdoc />
    public override SchemaRegistryContainer Build()
    {
        Validate();
        return new SchemaRegistryContainer(DockerResourceConfiguration);
    }

    /// <inheritdoc />
    protected override SchemaRegistryBuilder Init()
    {
        return base.Init()
            .WithImage(SchemaRegistryImage)
            .WithCleanUp(true)
            .WithExposedPort(SchemaRegistryPort)
            .WithPortBinding(SchemaRegistryPort, true)
            .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
            .WithEnvironment("SCHEMA_REGISTRY_LISTENERS", $"http://0.0.0.0:{SchemaRegistryPort}")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS", $"PLAINTEXT://kafka:{KafkaBuilder.BrokerPort}")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_SECURITY_PROTOCOL", "PLAINTEXT")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Server started, listening for requests..."));
    }

    /// <inheritdoc />
    protected override SchemaRegistryBuilder Clone(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new SchemaRegistryConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override SchemaRegistryBuilder Clone(IContainerConfiguration resourceConfiguration)
    {
        return Merge(DockerResourceConfiguration, new SchemaRegistryConfiguration(resourceConfiguration));
    }

    /// <inheritdoc />
    protected override SchemaRegistryBuilder Merge(SchemaRegistryConfiguration oldValue, SchemaRegistryConfiguration newValue)
    {
        return new SchemaRegistryBuilder(new SchemaRegistryConfiguration(oldValue, newValue));
    }
}
