using System.Threading.Channels;
using Confluent.Kafka;
using Google.Protobuf;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Messaging.Kafka.Options;

namespace Messaging.Kafka.Consumer;

internal sealed class KafkaConsumerWorker<T>(
    IKafkaConsumerFactory<T> consumerFactory,
    IMessageHandler<T> handler,
    ILogger<KafkaConsumerWorker<T>> logger) : BackgroundService
    where T : class, IMessage<T>, new()
{
    private readonly KafkaConsumerConfig _consumerConfig = consumerFactory.Config;
    private readonly int _handlerCount = Math.Max(1, consumerFactory.Config.ConcurrentMessageLimit ?? 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_consumerConfig.TopicPattern))
            throw new InvalidOperationException("No topic or pattern configured for KafkaConsumerWorker.");

        using IConsumer<string?, T> consumer = consumerFactory.Create();
        consumer.Subscribe(_consumerConfig.TopicPattern);

        logger.LogInformation("KafkaConsumerWorker<{Type}> started, subscribed to {TopicPattern}, handlers={Count}",
            typeof(T).Name, _consumerConfig.TopicPattern, _handlerCount);

        var channel = Channel.CreateBounded<ConsumeResult<string?, T>>(
            new BoundedChannelOptions(_handlerCount)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = true,
            });

        var consumeTask = Task.Run(() => RunConsumeLoopAsync(consumer, channel.Writer, stoppingToken));

        var handlerTasks = Enumerable.Range(0, _handlerCount)
            .Select(_ => RunHandlerLoopAsync(channel.Reader, consumer, stoppingToken))
            .ToArray();

        await Task.WhenAll([consumeTask, .. handlerTasks]);

        consumer.Close();

        logger.LogInformation("KafkaConsumerWorker<{Type}> stopped", typeof(T).Name);
    }

    private async Task RunConsumeLoopAsync(
        IConsumer<string?, T> consumer,
        ChannelWriter<ConsumeResult<string?, T>> writer,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string?, T> cr;
                try
                {
                    cr = consumer.Consume(ct);
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Consume error");
                    continue;
                }

                if (cr.IsPartitionEOF)
                    continue;

                await writer.WriteAsync(cr, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            writer.Complete();
        }
    }

    private async Task RunHandlerLoopAsync(
        ChannelReader<ConsumeResult<string?, T>> reader,
        IConsumer<string?, T> consumer,
        CancellationToken ct)
    {
        await foreach (var cr in reader.ReadAllAsync(ct))
        {
            try
            {
                logger.LogDebug(
                    "Consumer {Name} received message from {Topic} [{Partition}] @{Offset} key={Key}",
                    consumer.Name, cr.Topic, cr.Partition.Value, cr.Offset.Value, cr.Message.Key);

                await handler.HandleAsync(
                    cr.Message.Key, cr.Message.Value, cr.Topic, cr.Offset, cr.Partition, cr.Message.Headers);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Handler failed for message at [{Partition}]@{Offset} on {Topic}. Message will be skipped.",
                    cr.Partition.Value, cr.Offset.Value, cr.Topic);
            }

            consumer.StoreOffset(cr);
        }
    }
}
