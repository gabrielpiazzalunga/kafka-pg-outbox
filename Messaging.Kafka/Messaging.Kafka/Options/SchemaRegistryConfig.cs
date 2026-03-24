using System.Diagnostics.CodeAnalysis;

namespace Messaging.Kafka.Options;

[ExcludeFromCodeCoverage]
public sealed class SchemaRegistryConfig : IEquatable<SchemaRegistryConfig>
{
    public string? SchemaRegistryUrl { get; set; }
    public string? SchemaRegistryUsername { get; set; }
    public string? SchemaRegistryPassword { get; set; }

    public bool Equals(SchemaRegistryConfig? other)
    {
        if (other == null)
            return false;

        return
               SchemaRegistryUrl == other.SchemaRegistryUrl &&
               SchemaRegistryUsername == other.SchemaRegistryUsername &&
               SchemaRegistryPassword == other.SchemaRegistryPassword;
    }

    public override bool Equals(object? obj) => Equals(obj as SchemaRegistryConfig);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(SchemaRegistryUrl);
        hash.Add(SchemaRegistryUsername);
        hash.Add(SchemaRegistryPassword);
        return hash.ToHashCode();
    }

}
