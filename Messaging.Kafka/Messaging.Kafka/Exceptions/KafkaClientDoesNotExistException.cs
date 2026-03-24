namespace Messaging.Kafka.Exceptions
{
    public class KafkaClientDoesNotExistException(string message, Exception? innerException = null) : Exception(message, innerException)
    {
    }
}
