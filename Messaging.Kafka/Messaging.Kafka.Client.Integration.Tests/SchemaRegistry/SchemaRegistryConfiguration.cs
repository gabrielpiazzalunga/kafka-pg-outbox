using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;

namespace Messaging.Kafka.Client.Integration.Tests.SchemaRegistry;

/// <inheritdoc cref="ContainerConfiguration" />
public sealed class SchemaRegistryConfiguration : ContainerConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaRegistryConfiguration" /> class.
    /// </summary>
    public SchemaRegistryConfiguration()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaRegistryConfiguration" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public SchemaRegistryConfiguration(IResourceConfiguration<CreateContainerParameters> resourceConfiguration)
        : base(resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaRegistryConfiguration" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public SchemaRegistryConfiguration(IContainerConfiguration resourceConfiguration)
        : base(resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaRegistryConfiguration" /> class.
    /// </summary>
    /// <param name="resourceConfiguration">The Docker resource configuration.</param>
    public SchemaRegistryConfiguration(SchemaRegistryConfiguration resourceConfiguration)
        : this(new SchemaRegistryConfiguration(), resourceConfiguration)
    {
        // Passes the configuration upwards to the base implementations to create an updated immutable copy.
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SchemaRegistryConfiguration" /> class.
    /// </summary>
    /// <param name="oldValue">The old Docker resource configuration.</param>
    /// <param name="newValue">The new Docker resource configuration.</param>
    public SchemaRegistryConfiguration(SchemaRegistryConfiguration oldValue, SchemaRegistryConfiguration newValue)
        : base(oldValue, newValue)
    {
    }
}
