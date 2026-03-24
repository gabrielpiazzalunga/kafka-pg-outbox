using Confluent.Kafka;
using Google.Protobuf;
using Messaging.Kafka.Options;

namespace Messaging.Kafka.Consumer;

internal interface IKafkaConsumerFactory<T> where T : class, IMessage<T>, new()
{
    IConsumer<string?, T> Create();
    KafkaConsumerConfig Config { get; }
}
