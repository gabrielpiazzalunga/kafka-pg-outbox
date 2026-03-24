using Npgsql;

namespace Messaging.Kafka.Client.Infrastructure.Database;

internal sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public NpgsqlConnection CreateConnection() => new(connectionString);
}
