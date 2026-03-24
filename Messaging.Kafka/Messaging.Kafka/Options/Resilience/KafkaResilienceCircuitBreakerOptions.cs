using System.Diagnostics.CodeAnalysis;

namespace Messaging.Kafka.Options.Resilience;

[ExcludeFromCodeCoverage]
public sealed class KafkaResilienceCircuitBreakerOptions
{
    public int SamplingDuration { get; set; }
    public int MinimumThroughput { get; set; }
    public int BreakDuration { get; set; }
    public double FailureRatio { get; set; }
}
