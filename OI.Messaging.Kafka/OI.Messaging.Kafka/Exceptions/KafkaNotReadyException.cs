namespace OI.Messaging.Kafka.Exceptions
{
    public class KafkaNotReadyException(string message, Exception? innerException = null) : Exception(message, innerException)
    {
    }
}
