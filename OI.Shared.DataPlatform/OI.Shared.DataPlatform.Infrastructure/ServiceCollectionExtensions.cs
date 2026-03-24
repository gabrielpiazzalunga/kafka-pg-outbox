using Confluent.SchemaRegistry;
using Microsoft.Extensions.DependencyInjection;
using OI.Messaging.Kafka.Outbox;
using OI.Shared.DataPlatform.Infrastructure.Database;

namespace OI.Shared.DataPlatform.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDataPlatformInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string schemaRegistryUrl)
    {
        services.AddSingleton(new DatabaseMigrator(connectionString));
        services.AddSingleton<IDbConnectionFactory>(new NpgsqlConnectionFactory(connectionString));

        services.AddSingleton<ISchemaRegistryClient>(
            new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = schemaRegistryUrl }));
        services.AddSingleton<OutboxSerializer>();
        services.AddSingleton<AvroSchemaRegistrar>();

        services.AddHttpClient<DebeziumConnectorRegistrar>();

        return services;
    }
}
