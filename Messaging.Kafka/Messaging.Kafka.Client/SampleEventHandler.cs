using Confluent.Kafka;
using Messaging.Contracts.Proto;
using Messaging.Kafka.Consumer;

namespace Messaging.Kafka.Client;

/// <summary>
/// Normal mode handler to log consumed SampleEvents.
/// </summary>
internal sealed class SampleEventHandler : IMessageHandler<SampleEvent>
{
    public Task HandleAsync(string? key, SampleEvent message, string topic, Offset offset, Partition partition, Headers headers)
    {
        Console.WriteLine($"[SampleEventHandler] Consumed: {message.Id} - {message.Message}");
        return Task.CompletedTask;
    }
}
