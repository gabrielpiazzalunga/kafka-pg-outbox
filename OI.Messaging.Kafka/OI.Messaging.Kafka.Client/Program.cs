using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OI.Messaging.Contracts.Proto;
using OI.Messaging.Kafka;
using OI.Messaging.Kafka.Options;
using OI.Messaging.Kafka.Options.Resilience;
using OI.Messaging.Kafka.Producer;

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

KafkaConsumerConfig mbsEventCfg   = null!;
KafkaConsumerConfig mbsReadingCfg = null!;
KafkaConsumerConfig insReadingCfg = null!;

var host = Host.CreateDefaultBuilder(args)
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureServices((context, services) =>
    {
        var cfg = context.Configuration;
        var connection  = cfg.GetRequiredSection("Kafka:Connection").Get<KafkaConnectionConfig>()!;
        var producerCfg = cfg.GetRequiredSection("Kafka:ProducerConfig").Get<KafkaProducerConfig>()!;
        var resilience  = cfg.GetRequiredSection("Kafka:Resilience").Get<KafkaResilienceOptions>()!;
        mbsEventCfg   = cfg.GetRequiredSection("Kafka:MBESReadingEventConsumerConfig").Get<KafkaConsumerConfig>()!;
        mbsReadingCfg = cfg.GetRequiredSection("Kafka:MBESReadingConsumerConfig").Get<KafkaConsumerConfig>()!;
        insReadingCfg = cfg.GetRequiredSection("Kafka:INSReadingConsumerConfig").Get<KafkaConsumerConfig>()!;

        services.AddOIKafkaProducer(connection, producerCfg, resilience);

        if (loadCount.HasValue)
        {
            services.AddSingleton(new LoadTestConsumeTracker(loadCount.Value));
            services.AddOIKafkaConsumer<MBESReading, LoadTestHandler>(connection, mbsEventCfg);
        }
        else
        {
            services.AddOIKafkaConsumer<MBESReading, MBESReadingEventHandler>(connection, mbsEventCfg);
            services.AddOIKafkaConsumer<MBESReading, MBESReadingHandler>(connection, mbsReadingCfg);
            services.AddOIKafkaConsumer<INSReading, INSReadingHandler>(connection, insReadingCfg);
        }
    })
    .Build();

await host.StartAsync();

var publisher = new EventPublisher(
    host.Services.GetRequiredService<IKafkaProducer>(),
    mbsEventCfg.TopicPattern!,
    mbsReadingCfg.TopicPattern!,
    insReadingCfg.TopicPattern!);

if (loadCount.HasValue)
{
    var tracker = host.Services.GetRequiredService<LoadTestConsumeTracker>();
    await publisher.RunLoadTestAsync(loadCount.Value, tracker, CancellationToken.None);
    await host.StopAsync();
    return;
}

var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
_ = Task.Run(() => publisher.RunNormalModeAsync(lifetime.ApplicationStopping), lifetime.ApplicationStopping);
await host.WaitForShutdownAsync();
