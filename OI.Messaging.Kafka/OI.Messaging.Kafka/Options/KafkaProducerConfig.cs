using System.Diagnostics.CodeAnalysis;

namespace OI.Messaging.Kafka.Options;

[ExcludeFromCodeCoverage]
public sealed class KafkaProducerConfig : IEquatable<KafkaProducerConfig>
{
    public double? ProduceTimeout { get; set; }
    public string? ClientName { get; set; }

    // Batching / throughput knobs (map directly to librdkafka producer settings)
    // null = defer to librdkafka's own default for that setting
    public double? LingerMs                { get; set; }  // queue.buffering.max.ms
    public int?    BatchNumMessages        { get; set; }  // batch.num.messages
    public int?    QueueBufferingMaxKbytes { get; set; }  // queue.buffering.max.kbytes
    public int?    BatchSize               { get; set; }  // batch.size
    public string? CompressionType        { get; set; }  // compression.type

    public bool Equals(KafkaProducerConfig? other)
    {
        return other != null &&
               ProduceTimeout == other.ProduceTimeout &&
               ClientName == other.ClientName &&
               LingerMs == other.LingerMs &&
               BatchNumMessages == other.BatchNumMessages &&
               QueueBufferingMaxKbytes == other.QueueBufferingMaxKbytes &&
               BatchSize == other.BatchSize &&
               CompressionType == other.CompressionType;
    }

    public override bool Equals(object? obj) => Equals(obj as KafkaProducerConfig);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(ProduceTimeout);
        hash.Add(ClientName);
        hash.Add(LingerMs);
        hash.Add(BatchNumMessages);
        hash.Add(QueueBufferingMaxKbytes);
        hash.Add(BatchSize);
        hash.Add(CompressionType);
        return hash.ToHashCode();
    }
}
