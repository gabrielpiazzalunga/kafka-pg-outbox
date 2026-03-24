using Confluent.Kafka;
using OI.Messaging.Contracts.Proto;
using OI.Messaging.Kafka.Consumer;

internal sealed class INSReadingHandler : IMessageHandler<INSReading>
{
    public Task HandleAsync(string? key, INSReading msg, string topic, Offset offset, Partition partition, Headers headers)
    {
        Console.WriteLine(
            $"[INSReading]  [{partition}]@{offset} | vessel={msg.Envelope?.VesselId} " +
            $"payload={msg.Envelope?.PayloadId} lat={msg.LatitudeDeg:F6} lon={msg.LongitudeDeg:F6} hdg={msg.HeadingDeg:F1}°");
        return Task.CompletedTask;
    }
}
