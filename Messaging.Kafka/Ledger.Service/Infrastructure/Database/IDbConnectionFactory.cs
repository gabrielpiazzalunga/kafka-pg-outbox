using Npgsql;

namespace Ledger.Service.Infrastructure.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}
