using System.Diagnostics.CodeAnalysis;

namespace Messaging.Kafka.Options.Resilience;

[ExcludeFromCodeCoverage]
public sealed class KafkaResilienceOptions
{
    public KafkaResilienceRetryOptions? Retry { get; set; } = null;
    public KafkaResilienceCircuitBreakerOptions? CircuitBreaker { get; set; }
    public KafkaResilienceTimeoutOptions? Timeout { get; set; }
}
