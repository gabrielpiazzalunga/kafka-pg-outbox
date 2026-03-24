using Confluent.Kafka;
using Google.Protobuf;
using OI.Messaging.Kafka.Options;

namespace OI.Messaging.Kafka.Consumer;

internal interface IKafkaConsumerFactory<T> where T : class, IMessage<T>, new()
{
    IConsumer<string?, T> Create();
    KafkaConsumerConfig Config { get; }
}
