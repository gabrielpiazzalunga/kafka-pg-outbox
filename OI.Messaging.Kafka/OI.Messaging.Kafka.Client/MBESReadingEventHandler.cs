using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using OI.Messaging.Contracts.Proto;
using OI.Messaging.Kafka.Consumer;

public sealed class MBESReadingEventHandler(ILogger<MBESReadingEventHandler> logger) : IMessageHandler<MBESReading>
{
    private readonly ILogger<MBESReadingEventHandler> _logger = logger;

    public Task HandleAsync(string? key, MBESReading message, string topic, Offset offset, Partition partition, Headers headers)
    {
        _logger.LogInformation(
            "Received MBESReading from {Topic} [{Partition}]@{Offset} | Key={Key} | PayloadId={PayloadId} | Ts={Ts}",
            topic, partition.Value, offset.Value, key, message.Envelope?.PayloadId, message.Envelope?.Ts);

        return Task.CompletedTask;
    }
}
