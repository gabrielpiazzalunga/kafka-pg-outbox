using System.Diagnostics.CodeAnalysis;

namespace OI.Messaging.Kafka.Options;

[ExcludeFromCodeCoverage]
public sealed class KafkaConnectionConfig : IEquatable<KafkaConnectionConfig>
{
    public string? BootstrapServer { get; set; }
    public string? SaslUsername { get; set; }
    public string? SaslPassword { get; set; }
    public string? SchemaRegistryUrl { get; set; }
    public string? SchemaRegistryUsername { get; set; }
    public string? SchemaRegistryPassword { get; set; }

    public bool Equals(KafkaConnectionConfig? other)
    {
        if (other == null)
            return false;


        return
               BootstrapServer == other.BootstrapServer &&
               SaslUsername == other.SaslUsername &&
               SaslPassword == other.SaslPassword &&
               SchemaRegistryUrl == other.SchemaRegistryUrl &&
               SchemaRegistryUsername == other.SchemaRegistryUsername &&
               SchemaRegistryPassword == other.SchemaRegistryPassword;
    }

    public override bool Equals(object? obj) => Equals(obj as KafkaConnectionConfig);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(BootstrapServer);
        hash.Add(SaslUsername);
        hash.Add(SaslPassword);
        hash.Add(SchemaRegistryUrl);
        hash.Add(SchemaRegistryUsername);
        hash.Add(SchemaRegistryPassword);
        return hash.ToHashCode();
    }


}
