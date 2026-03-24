using Npgsql;

namespace FeedIngestion.Service.Infrastructure.Database;

internal sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public NpgsqlConnection CreateConnection() => new(connectionString);
}
