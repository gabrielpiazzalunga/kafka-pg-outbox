using Npgsql;

namespace OI.Shared.DataPlatform.Infrastructure.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}
