using Confluent.Kafka;
using FluentResults;
using Google.Protobuf;
using Polly;
using Polly.Registry;
using Messaging.Kafka.Resilience;

namespace Messaging.Kafka.Producer;

internal sealed class ResilientKafkaProducer(IKafkaProducerClient inner, ResiliencePipelineProvider<string> resiliencePipeline) : IKafkaProducer
{
    private readonly ResiliencePipeline _pipeline = resiliencePipeline.GetPipeline(KafkaPipelines.Kafka);

    public async Task<Result> ProduceConfirmedAsync<T>(T record, string topic, Headers headers, string? key = null)
        where T : class, IMessage<T>, new()
    {
        if (string.IsNullOrWhiteSpace(topic))
            return Result.Fail("No topic provided");

        await _pipeline.ExecuteAsync(
            async ct => await inner.ProduceConfirmedAsync(record, topic, headers, key),
            CancellationToken.None);

        return Result.Ok();
    }

    public async Task<Result> ProduceAsync(byte[] value, string topic, Headers headers)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return Result.Fail("No topic provided");

        await _pipeline.ExecuteAsync(
            async ct => await inner.ProduceAsync(value, topic, headers),
            CancellationToken.None);

        return Result.Ok();
    }

    public async Task<Result> ProduceBatchAsync<T>(IReadOnlyList<(T Record, string? Key)> messages, string topic, Headers headers)
        where T : class, IMessage<T>, new()
    {
        if (string.IsNullOrWhiteSpace(topic))
            return Result.Fail("No topic provided");

        await _pipeline.ExecuteAsync(
            async ct => await inner.ProduceBatchAsync(messages, topic, headers),
            CancellationToken.None);

        return Result.Ok();
    }

    void IKafkaProducer.ProduceFireForget<T>(T record, string topic, Headers headers, string? key) =>
        _pipeline.Execute(() => inner.ProduceFireForget(record, topic, headers, key));
}
