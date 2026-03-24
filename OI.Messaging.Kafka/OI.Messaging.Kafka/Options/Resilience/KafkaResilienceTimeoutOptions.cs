using System.Diagnostics.CodeAnalysis;

namespace OI.Messaging.Kafka.Options.Resilience;

[ExcludeFromCodeCoverage]
public sealed class KafkaResilienceTimeoutOptions
{
    public int Timeout { get; set; }
}
