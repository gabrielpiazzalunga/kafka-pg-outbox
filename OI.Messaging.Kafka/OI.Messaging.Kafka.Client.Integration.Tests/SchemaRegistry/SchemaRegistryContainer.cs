using DotNet.Testcontainers.Containers;

namespace OI.Messaging.Kafka.Client.Integration.Tests.SchemaRegistry;

/// <inheritdoc cref="DockerContainer" />
public sealed class SchemaRegistryContainer : DockerContainer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="KafkaContainer" /> class.
    /// </summary>
    /// <param name="configuration">The container configuration.</param>
    public SchemaRegistryContainer(SchemaRegistryConfiguration configuration)
        : base(configuration)
    {
    }

    /// <summary>
    /// Gets the broker address.
    /// </summary>
    /// <returns>The broker address.</returns>
    public string GetBootstrapAddress()
    {
        return $"{Hostname}:{GetMappedPublicPort(SchemaRegistryBuilder.SchemaRegistryPort)}";
    }
}
