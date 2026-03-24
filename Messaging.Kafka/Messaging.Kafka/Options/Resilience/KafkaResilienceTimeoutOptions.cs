using System.Diagnostics.CodeAnalysis;

namespace Messaging.Kafka.Options.Resilience;

[ExcludeFromCodeCoverage]
public sealed class KafkaResilienceTimeoutOptions
{
    public int Timeout { get; set; }
}
