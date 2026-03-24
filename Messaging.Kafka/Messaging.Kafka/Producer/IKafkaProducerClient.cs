using Confluent.Kafka;
using FluentResults;
using Google.Protobuf;

namespace Messaging.Kafka.Producer;

internal interface IKafkaProducerClient
{
    Task<Result> ProduceConfirmedAsync<T>(T record, string topic, Headers headers, string? key = null)
        where T : class, IMessage<T>, new();
    void ProduceFireForget<T>(T record, string topic, Headers headers, string? key = null)
    where T : class, IMessage<T>, new();
    Task<Result> ProduceAsync(byte[] value, string topic, Headers headers);
    Task<Result> ProduceBatchAsync<T>(IReadOnlyList<(T Record, string? Key)> messages, string topic, Headers headers)
        where T : class, IMessage<T>, new();
}
