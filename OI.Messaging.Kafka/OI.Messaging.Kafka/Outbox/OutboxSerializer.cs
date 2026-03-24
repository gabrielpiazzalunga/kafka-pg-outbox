using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using Google.Protobuf;

namespace OI.Messaging.Kafka.Outbox;

/// <summary>
/// Serializes messages (Protobuf or Avro) into byte arrays with the Confluent Schema Registry
/// 5-byte Magic Header. The resulting bytes are identical to what a native Confluent 
/// producer würde send, making them compatible with Debezium outbox extraction.
/// </summary>
public sealed class OutboxSerializer(ISchemaRegistryClient schemaRegistry)
{
    /// <summary>
    /// Serializes a Protobuf message to a byte array.
    /// </summary>
    public async Task<byte[]> SerializeProtobufAsync<T>(T message, string topic) 
        where T : class, IMessage<T>, new()
    {
        var serializer = new ProtobufSerializer<T>(schemaRegistry);
        var context = new SerializationContext(MessageComponentType.Value, topic);
        return await serializer.SerializeAsync(message, context);
    }

    /// <summary>
    /// Serializes an Avro message (using a specific generated class or GenericRecord) to a byte array.
    /// </summary>
    public async Task<byte[]> SerializeAvroAsync<T>(T message, string topic) 
        where T : class
    {
        var serializer = new AvroSerializer<T>(schemaRegistry);
        var context = new SerializationContext(MessageComponentType.Value, topic);
        return await serializer.SerializeAsync(message, context);
    }
}
