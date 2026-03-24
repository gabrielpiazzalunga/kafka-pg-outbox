using Npgsql;

namespace OI.Shared.DataPlatform.Infrastructure.Database;

internal sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public NpgsqlConnection CreateConnection() => new(connectionString);
}
