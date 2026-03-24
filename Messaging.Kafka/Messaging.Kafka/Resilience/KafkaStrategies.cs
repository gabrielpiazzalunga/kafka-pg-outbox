using System.Collections.Immutable;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using Messaging.Kafka.Options.Resilience;

namespace Messaging.Kafka.Resilience;

public sealed class KafkaStrategies
{
    private static readonly ImmutableArray<Type> s_nonRetryableExceptions = new[]
    {
        typeof(BrokenCircuitException),
    }.ToImmutableArray();

    public static RetryStrategyOptions GetRetryConfiguration(KafkaResilienceRetryOptions retryOptions)
    {
        return new RetryStrategyOptions()
        {
            Delay = TimeSpan.FromSeconds(retryOptions.Delay),
            MaxRetryAttempts = retryOptions.MaxRetryAttempts,
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = ex => new ValueTask<bool>(ex.Outcome.Exception is not null
                                                    && !s_nonRetryableExceptions.Contains(ex.Outcome.Exception.GetType())),
            OnRetry = static args =>
            {
                Console.WriteLine("OnRetry, Attempt: {0}", args.AttemptNumber);

                // Event handlers can be asynchronous; here, we return an empty ValueTask.
                return default;
            }
        };
    }

    public static CircuitBreakerStrategyOptions GetCircuitBreakerConfiguration(KafkaResilienceCircuitBreakerOptions circuitBreakerOptions)
    {
        return new CircuitBreakerStrategyOptions
        {
            SamplingDuration = TimeSpan.FromSeconds(circuitBreakerOptions.SamplingDuration),
            FailureRatio = circuitBreakerOptions.FailureRatio,
            MinimumThroughput = circuitBreakerOptions.MinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(circuitBreakerOptions.BreakDuration),
            ShouldHandle = ex => new ValueTask<bool>(ex.Outcome.Exception is not null)
        };
    }

    public static TimeoutStrategyOptions GetTimeoutConfiguration(KafkaResilienceTimeoutOptions timeoutOptions) => new()
    {
        Timeout = TimeSpan.FromSeconds(timeoutOptions.Timeout),
    };
}
