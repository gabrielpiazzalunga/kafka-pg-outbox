using System.Diagnostics.CodeAnalysis;

namespace Messaging.Kafka.Options.Resilience;

[ExcludeFromCodeCoverage]
public sealed class KafkaResilienceRetryOptions
{
    public int MaxRetryAttempts { get; set; }
    public int Delay { get; set; }
}
