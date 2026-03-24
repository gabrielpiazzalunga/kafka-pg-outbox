using Confluent.SchemaRegistry;
using Microsoft.Extensions.DependencyInjection;
using Messaging.Kafka.Outbox;
using Messaging.Kafka.Client.Infrastructure.Database;

namespace Messaging.Kafka.Client.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOutboxInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string schemaRegistryUrl)
    {
        services.AddSingleton(new DatabaseMigrator(connectionString));
        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        services.AddSingleton<ISchemaRegistryClient>(
            new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = schemaRegistryUrl }));
        services.AddSingleton<OutboxSerializer>();

        services.AddHttpClient<DebeziumConnectorRegistrar>();

        return services;
    }
}
