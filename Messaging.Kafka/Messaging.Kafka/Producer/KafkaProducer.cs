using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Confluent.SchemaRegistry.Serdes;
using FluentResults;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Messaging.Kafka.Common;
using Messaging.Kafka.Exceptions;
using Messaging.Kafka.Options;

namespace Messaging.Kafka.Producer;

public sealed class KafkaProducer : IKafkaProducerClient, IDisposable
{
    private const double DefaultProduceTimeout = 20;

    private readonly KafkaProducerConfig _producerConfig;
    private readonly ILogger<KafkaProducer> _logger;
    private readonly IProducer<Null, byte[]> _rawProducer;
    private readonly ISchemaRegistryClient _schemaRegistryClient;
    private readonly ProducerConfig _config;
    private readonly ConcurrentDictionary<Type, IDisposable> _protobufProducers = new();

    public KafkaProducer(
        IOptions<KafkaConnectionConfig> connectionOptions,
        IOptions<KafkaProducerConfig> producerConfigOptions,
        ISchemaRegistryClient schemaRegistryClient,
        ILogger<KafkaProducer> logger)
    {
        var connection = connectionOptions.Value;
        _producerConfig = producerConfigOptions.Value;
        _logger = logger;
        _schemaRegistryClient = schemaRegistryClient;

        var clientId = Guid.NewGuid().ToString();
        _config = KafkaUtility.BuildProducerConfig(connection, _producerConfig, clientId);

        _rawProducer = new ProducerBuilder<Null, byte[]>(_config).Build();
    }

    public async Task<Result> ProduceConfirmedAsync<T>(T record, string topic, Headers headers, string? key = null)
        where T : class, IMessage<T>, new()
    {
        if (string.IsNullOrWhiteSpace(topic))
            return Result.Fail("No topic provided");

        _logger.LogDebug("ProduceConfirmedAsync to topic {Topic} key={Key}", topic, key);

        var producer = (IProducer<string?, T>)_protobufProducers.GetOrAdd(typeof(T), _ =>
            new ProducerBuilder<string?, T>(_config)
                .SetKeySerializer(NullableStringSerializer.Instance)
                .SetValueSerializer(new ProtobufSerializer<T>(_schemaRegistryClient))
                .Build());

        await producer.ProduceAsync(topic, new Message<string?, T> { Key = key, Value = record, Headers = headers })
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogError("Error producing Protobuf message key={Key} topic={Topic}: {Error}", key, topic, task.Exception!.Message);
                    throw new WriteException($"Error producing message: {task.Exception!.Message}", task.Exception);
                }
            });

        return Result.Ok();
    }

    public async Task<Result> ProduceAsync(byte[] value, string topic, Headers headers)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return Result.Fail("No topic provided");

        _logger.LogDebug("ProduceAsync to topic {Topic}", topic);

        await _rawProducer.ProduceAsync(topic, new Message<Null, byte[]> { Value = value, Headers = headers })
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogError("Error producing message from producer {Name}", _rawProducer.Name);
                    throw new WriteException($"Error producing message: {task.Exception!.Message} from producer {_rawProducer.Name}", task.Exception);
                }
            });

        return Result.Ok();
    }

    public void Dispose()
    {
        var timeout = TimeSpan.FromSeconds(_producerConfig.ProduceTimeout ?? DefaultProduceTimeout);

        foreach (var p in _protobufProducers.Values)
            p.Dispose();

        _rawProducer.Flush(timeout);
        _rawProducer.Dispose();
    }

    public async Task<Result> ProduceBatchAsync<T>(IReadOnlyList<(T Record, string? Key)> messages, string topic, Headers headers)
        where T : class, IMessage<T>, new()
    {
        if (string.IsNullOrWhiteSpace(topic))
            return Result.Fail("No topic provided");

        if (messages.Count == 0)
            return Result.Ok();
        var producer = (IProducer<string?, T>)_protobufProducers.GetOrAdd(typeof(T), _ =>
            new ProducerBuilder<string?, T>(_config)
                .SetKeySerializer(NullableStringSerializer.Instance)
                .SetValueSerializer(new ProtobufSerializer<T>(_schemaRegistryClient))
                .Build());

        var tasks = messages.Select(async m =>
        {
            try
            {
                await producer.ProduceAsync(topic, new Message<string?, T> { Key = m.Key, Value = m.Record, Headers = headers });
                return (string?)null;
            }
            catch (Exception ex)
            {
                _logger.LogError("Batch produce error key={Key} topic={Topic}: {Error}", m.Key, topic, ex.Message);
                return ex.Message;
            }
        });

        var errors = (await Task.WhenAll(tasks)).OfType<string>().ToList();
        return errors.Count == 0
            ? Result.Ok()
            : Result.Fail(errors.Select(e => (IError)new FluentResults.Error(e)).ToList());
    }

    public void ProduceFireForget<T>(T record, string topic, Headers headers, string? key = null)
        where T : class, IMessage<T>, new()
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            _logger.LogWarning("ProduceFireAndForget: no topic provided, message discarded");
            return;
        }

        _logger.LogDebug("ProduceConfirmedAsync to topic {Topic} key={Key}", topic, key);

        var producer = (IProducer<string?, T>)_protobufProducers.GetOrAdd(typeof(T), _ =>
            new ProducerBuilder<string?, T>(_config)
                .SetKeySerializer(NullableStringSerializer.Instance)
                .SetValueSerializer(new ProtobufSerializer<T>(_schemaRegistryClient))
                .Build());

        _ = producer.ProduceAsync(topic, new Message<string?, T> { Key = key, Value = record, Headers = headers })
            .ContinueWith(task =>
            {
                if (task.IsFaulted)
                {
                    _logger.LogError("Error producing Protobuf message key={Key} topic={Topic}: {Error}", key, topic, task.Exception!.Message);
                    throw new WriteException($"Error producing message: {task.Exception!.Message}", task.Exception);
                }
            });
    }

    private sealed class NullableStringSerializer : ISerializer<string?>
    {
        public static readonly NullableStringSerializer Instance = new();

        private NullableStringSerializer() { }

        public byte[] Serialize(string? data, SerializationContext _) =>
            data == null ? [] : Encoding.UTF8.GetBytes(data);
    }
}
