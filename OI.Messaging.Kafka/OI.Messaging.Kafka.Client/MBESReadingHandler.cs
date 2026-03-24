using Confluent.Kafka;
using OI.Messaging.Contracts.Proto;
using OI.Messaging.Kafka.Consumer;

internal sealed class MBESReadingHandler : IMessageHandler<MBESReading>
{
    public Task HandleAsync(string? key, MBESReading msg, string topic, Offset offset, Partition partition, Headers headers)
    {
        Console.WriteLine(
            $"[MBESReading] [{partition}]@{offset} | vessel={msg.Envelope?.VesselId} " +
            $"payload={msg.Envelope?.PayloadId} depth={msg.DepthM:F1}m swath={msg.SwathWidthM:F1}m");
        return Task.CompletedTask;
    }
}
