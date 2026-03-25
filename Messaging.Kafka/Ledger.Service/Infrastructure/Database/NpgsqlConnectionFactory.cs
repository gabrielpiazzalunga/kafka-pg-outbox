using Npgsql;

namespace Ledger.Service.Infrastructure.Database;

public sealed class NpgsqlConnectionFactory(string connectionString) : IDbConnectionFactory
{
    public NpgsqlConnection CreateConnection() => new(connectionString);
}
