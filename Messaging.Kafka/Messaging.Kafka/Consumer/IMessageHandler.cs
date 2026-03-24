using Confluent.Kafka;

namespace Messaging.Kafka.Consumer;

public interface IMessageHandler<in T>
{
    Task HandleAsync(string? key, T @event, string topic, Offset offset, Partition partition, Headers headers);
}
