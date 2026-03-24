using System.Diagnostics.CodeAnalysis;

namespace OI.Messaging.Kafka.Options.Resilience;

[ExcludeFromCodeCoverage]
public sealed class KafkaResilienceRetryOptions
{
    public int MaxRetryAttempts { get; set; }
    public int Delay { get; set; }
}
