namespace Messaging.Kafka.Exceptions
{
    public class WriteException(string message, Exception exception) : Exception(message, exception)
    {
    }
}
