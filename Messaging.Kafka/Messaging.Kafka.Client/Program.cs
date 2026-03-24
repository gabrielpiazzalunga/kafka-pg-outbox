using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Messaging.Contracts.Proto;
using Messaging.Kafka;
using Messaging.Kafka.Options;
using Messaging.Kafka.Options.Resilience;
using Messaging.Kafka.Client;
using Messaging.Kafka.Client.Infrastructure;
using Messaging.Kafka.Client.Infrastructure.Database;
using Messaging.Kafka.Outbox;

// Parse --load <N> — if present, run a throughput benchmark instead of the normal loop
int? loadCount = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--load" && int.TryParse(args[i + 1], out int n) && n > 0)
    {
        loadCount = n;
        break;
    }
}

KafkaConsumerConfig sampleEventCfg = null!;

var host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureServices((context, services) =>
    {
        var cfg = context.Configuration;
        var connection = cfg.GetRequiredSection("Kafka:Connection").Get<KafkaConnectionConfig>()!;
        sampleEventCfg = cfg.GetRequiredSection("Kafka:SampleEventConsumerConfig").Get<KafkaConsumerConfig>()!;

        var pgConnectionString = cfg.GetSection("Postgres")["ConnectionString"]!;
        services.AddOutboxInfrastructure(pgConnectionString, connection.SchemaRegistryUrl!);
        services.AddHostedService<OutboxDemoWorker>();

        if (loadCount.HasValue)
        {
            services.AddSingleton(new LoadTestConsumeTracker(loadCount.Value));
            services.AddKafkaConsumer<SampleEvent, LoadTestHandler>(connection, sampleEventCfg);
        }
        else
        {
            services.AddKafkaConsumer<SampleEvent, SampleEventHandler>(connection, sampleEventCfg);
        }
    })
    .Build();

await host.StartAsync();

var migrator = host.Services.GetRequiredService<DatabaseMigrator>();
migrator.MigrateUp();

var publisher = new EventPublisher(
    host.Services.GetRequiredService<IDbConnectionFactory>(),
    host.Services.GetRequiredService<OutboxSerializer>());

if (loadCount.HasValue)
{
    var tracker = host.Services.GetRequiredService<LoadTestConsumeTracker>();
    await publisher.RunLoadTestAsync(loadCount.Value, tracker, CancellationToken.None);
    await host.StopAsync();
    return;
}

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
// Normal mode: The OutboxDemoWorker inserts events every 5s automatically. We just keep the host running.
await host.WaitForShutdownAsync();
