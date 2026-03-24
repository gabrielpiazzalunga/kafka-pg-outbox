using Npgsql;

namespace FeedIngestion.Service.Infrastructure.Database;

public interface IDbConnectionFactory
{
    NpgsqlConnection CreateConnection();
}
