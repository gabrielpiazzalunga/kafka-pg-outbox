using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.SyncOverAsync;
using Confluent.SchemaRegistry.Serdes;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using OI.Messaging.Kafka.Common;
using OI.Messaging.Kafka.Options;

namespace OI.Messaging.Kafka.Consumer;

internal sealed class KafkaConsumerFactory<T>(
    IOptions<KafkaConnectionConfig> connectionOptions,
    KafkaConsumerConfig consumerConfig)
    : IKafkaConsumerFactory<T>
    where T : class, IMessage<T>, new()
{
    private readonly KafkaConnectionConfig _connection = connectionOptions.Value;
    public KafkaConsumerConfig Config { get; } = consumerConfig;

    public IConsumer<string?, T> Create() =>
        new ConsumerBuilder<string?, T>(KafkaUtility.BuildConsumerConfig(_connection, Config))
            .SetKeyDeserializer(NullableStringDeserializer.Instance)
            .SetValueDeserializer(new ProtobufDeserializer<T>().AsSyncOverAsync())
            .Build();

    /// <summary>
    /// Deserializes a Kafka message key as a nullable UTF-8 string.
    /// Unlike <see cref="Deserializers.Utf8"/>, returns <c>null</c> for a null/absent key
    /// instead of an empty string, preserving the Kafka semantic distinction.
    /// </summary>
    private sealed class NullableStringDeserializer : IDeserializer<string?>
    {
        public static readonly NullableStringDeserializer Instance = new();

        private NullableStringDeserializer() { }

        public string? Deserialize(ReadOnlySpan<byte> data, bool isNull, SerializationContext _) =>
            isNull ? null : Encoding.UTF8.GetString(data);
    }
}
