using Npgsql;

namespace Messaging.Kafka.Client.Infrastructure.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}
