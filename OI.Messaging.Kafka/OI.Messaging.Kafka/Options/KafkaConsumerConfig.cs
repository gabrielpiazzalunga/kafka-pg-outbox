using System.Diagnostics.CodeAnalysis;

namespace OI.Messaging.Kafka.Options;

[ExcludeFromCodeCoverage]
public sealed class KafkaConsumerConfig
{
    /// <summary>
    /// Literal topic name or a regex pattern (prefix with <c>^</c>, e.g. <c>^myapp\..*</c>).
    /// When a pattern is used, all matching topics receive messages of the same type <c>T</c>.
    /// </summary>
    public string? TopicPattern { get; set; }
    public string? ConsumerGroupId { get; set; }
    public string? ClientId { get; set; }
    /// <summary>
    /// Where to start reading when the consumer group has no committed offset.
    /// Accepted values: "Latest" (default), "Earliest", "Error".
    /// </summary>
    public string AutoOffsetReset { get; set; } = "Latest";
    /// <summary>
    /// Maximum number of messages processed concurrently for this consumer.
    /// Defaults to 1 (sequential).
    /// </summary>
    public int? ConcurrentMessageLimit { get; set; }
}
