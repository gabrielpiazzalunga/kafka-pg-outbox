using Confluent.Kafka;
using Confluent.SchemaRegistry;
using Messaging.Kafka.Options;
using SchemaRegistryConfig = Confluent.SchemaRegistry.SchemaRegistryConfig;

namespace Messaging.Kafka.Common;

public static class KafkaUtility
{
    public static bool UseAuthentication(string? username, string? password)
    {
        var isUsernameEmpty = string.IsNullOrEmpty(username) || username.Equals(".");
        var isPasswordEmpty = string.IsNullOrEmpty(password) || password.Equals(".");
        return !(isUsernameEmpty | isPasswordEmpty);
    }

    public static ProducerConfig BuildProducerConfig(KafkaConnectionConfig connection, KafkaProducerConfig producerConfig, string clientId)
    {
        var compression = ParseCompressionType(producerConfig.CompressionType);

        if (UseAuthentication(connection.SaslUsername, connection.SaslPassword))
        {
            return new ProducerConfig
            {
                BootstrapServers = connection.BootstrapServer,
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = connection.SaslUsername,
                SaslPassword = connection.SaslPassword,
                ClientId = string.Join("_", producerConfig.ClientName, clientId),
                LingerMs = producerConfig.LingerMs,
                BatchNumMessages = producerConfig.BatchNumMessages,
                QueueBufferingMaxKbytes = producerConfig.QueueBufferingMaxKbytes,
                BatchSize = producerConfig.BatchSize,
                CompressionType = compression,
            };
        }

        return new ProducerConfig
        {
            BootstrapServers = connection.BootstrapServer,
            ClientId = string.Join("_", producerConfig.ClientName, clientId),
            LingerMs = producerConfig.LingerMs,
            BatchNumMessages = producerConfig.BatchNumMessages,
            QueueBufferingMaxKbytes = producerConfig.QueueBufferingMaxKbytes,
            BatchSize = producerConfig.BatchSize,
            CompressionType = compression,
        };
    }

    public static ConsumerConfig BuildConsumerConfig(KafkaConnectionConfig connection, KafkaConsumerConfig config)
    {
        string clientId = config.ClientId ?? Guid.NewGuid().ToString();
        AutoOffsetReset autoOffsetReset = config.AutoOffsetReset.ToUpperInvariant() switch
        {
            "LATEST" => AutoOffsetReset.Latest,
            "ERROR" => AutoOffsetReset.Error,
            _ => AutoOffsetReset.Earliest
        };

        if (UseAuthentication(connection.SaslUsername, connection.SaslPassword))
        {
            return new ConsumerConfig
            {
                GroupId = config.ConsumerGroupId,
                BootstrapServers = connection.BootstrapServer,
                SecurityProtocol = SecurityProtocol.SaslSsl,
                SaslMechanism = SaslMechanism.Plain,
                SaslUsername = connection.SaslUsername,
                SaslPassword = connection.SaslPassword,
                ClientId = clientId,
                EnableAutoOffsetStore = false,
                AutoOffsetReset = autoOffsetReset
            };
        }

        return new ConsumerConfig
        {
            GroupId = config.ConsumerGroupId,
            BootstrapServers = connection.BootstrapServer,
            ClientId = clientId,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = autoOffsetReset
        };
    }

    public static SchemaRegistryConfig BuildSchemaRegistryConfig(KafkaConnectionConfig connection)
    {
        if (UseAuthentication(connection.SchemaRegistryUsername, connection.SchemaRegistryPassword))
        {
            return new SchemaRegistryConfig
            {
                Url = connection.SchemaRegistryUrl,
                BasicAuthCredentialsSource = AuthCredentialsSource.UserInfo,
                BasicAuthUserInfo = string.Join(":", connection.SchemaRegistryUsername, connection.SchemaRegistryPassword)
            };
        }

        return new SchemaRegistryConfig
        {
            Url = connection.SchemaRegistryUrl
        };
    }

    private static CompressionType? ParseCompressionType(string? value) =>
        value?.ToUpperInvariant() switch
        {
            "GZIP"   => CompressionType.Gzip,
            "SNAPPY" => CompressionType.Snappy,
            "LZ4"    => CompressionType.Lz4,
            "ZSTD"   => CompressionType.Zstd,
            "NONE"   => CompressionType.None,
            _        => null,
        };
}
