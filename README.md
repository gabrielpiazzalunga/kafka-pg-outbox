# OI.Messaging.Kafka

A reusable .NET package for producing messages to Kafka with Avro schema registry support and built-in resilience (retry, circuit breaker, timeout via Polly).

## Prerequisites

- .NET 9 SDK
- A running Kafka broker and Schema Registry (see [Local Development](#local-development))

---

## Local Development

The `eng/manifests/` directory contains Kubernetes manifests that spin up a self-contained local environment with:

- **Zookeeper** — coordination service required by the Kafka broker
- **Kafka** — the message broker, exposed on `localhost:9092`
- **Schema Registry** — stores and validates Avro schemas, exposed on `localhost:8081`
- **Control Center** — Confluent web UI for inspecting topics and messages, exposed on `localhost:9021`
- **init-topics** — a one-off job that pre-creates the expected `localdev.*` topics on startup

Apply the full stack from the repo root:

```bash
kubectl apply -k eng/manifests/
```

To tear it down:

```bash
kubectl delete -k eng/manifests/
```

### Useful commands

Check the status of all deployed resources:

```bash
kubectl get all
```

Inspect a specific pod (useful when a pod is stuck in `Pending` or `CrashLoopBackOff`):

```bash
kubectl describe pod <pod-name>
```

Stream logs from a running pod:

```bash
kubectl logs <pod-name>
# Follow live output
kubectl logs -f <pod-name>
```

> Tip: get pod names quickly with `kubectl get pods`.

---

Expected config (`appsettings.json`):

```json
{
  "Kafka": {
    "Connection": {
      "BootstrapServer": "localhost:9092",
      "SchemaRegistryUrl": "http://localhost:8081",
      "SaslUsername": ".",
      "SaslPassword": "."
    },
    "ProducerConfig": {
      "ClientName": "my-service",
      "ProduceTimeout": 20
    },
    "Resilience": {
      "Timeout": { "Timeout": 30 },
      "Retry": { "MaxRetryAttempts": 3, "Delay": 2 },
      "CircuitBreaker": {
        "SamplingDuration": 60,
        "FailureRatio": 0.5,
        "MinimumThroughput": 5,
        "BreakDuration": 30
      }
    }
  }
}
```

> **Note:** Set `SaslUsername` and `SaslPassword` to `"."` to disable authentication (local dev).

---

## Producer batching

librdkafka batches messages internally before sending them over the network. These settings control
the trade-off between throughput (larger batches) and latency (send sooner).

Full `ProducerConfig` section with all batching knobs:

```json
"ProducerConfig": {
  "ClientName": "my-service",
  "ProduceTimeout": 20,
  "LingerMs": 5,
  "BatchNumMessages": 10000,
  "QueueBufferingMaxKbytes": 65536,
  "BatchSize": 65536,
  "CompressionType": "lz4"
}
```

All fields are optional. Omitting a field leaves the librdkafka default in effect.

| Field | librdkafka name | Kafka default | Description |
|---|---|---|---|
| `LingerMs` | `queue.buffering.max.ms` | `0` | Max milliseconds to wait before flushing the send queue. `0` = send immediately. Higher = larger batches, more latency. |
| `BatchNumMessages` | `batch.num.messages` | `10000` | Max messages per batch (flush triggered regardless of linger when reached). |
| `QueueBufferingMaxKbytes` | `queue.buffering.max.kbytes` | `1048576` (1 GB) | Max total bytes held in the send queue. |
| `BatchSize` | `batch.size` | `1000000` (~1 MB) | Max bytes per per-partition batch. |
| `CompressionType` | `compression.type` | `none` | Batch compression. `lz4` is fast with a good ratio. Options: `none`, `gzip`, `snappy`, `lz4`, `zstd`. |

> **Latency vs throughput:** at Kafka's default (`LingerMs` omitted or `0`) each message is sent
> immediately — lowest latency, no batching. Set `LingerMs: 5` to wait up to 5 ms and accumulate
> messages into a batch — ideal for bulk or high-throughput producers.

### The `LingerMs` pitfall — sequential `await`

Setting `LingerMs` has **no effect** when you `await` each produce call in a loop:

```csharp
// Each call waits for the broker ACK before enqueuing the next message.
// Every message sits alone in the buffer for the full linger window — no batching happens.
for each event:
    await producer.ProduceAvroConfirmedAsync(evt, topic, headers, key);
// result: ~(LingerMs + broker RTT) per message
```

Two alternatives avoid this pitfall:

**`ProduceFireAndForget`** — enqueues each message and returns immediately without waiting for
the broker ACK. Use in a tight loop: librdkafka accumulates messages and flushes them as a batch.
Delivery errors surface only in logs. The producer flushes all outstanding messages on `Dispose`.

```csharp
foreach (var evt in events)
    producer.ProduceFireAndForget(evt, topic, new Headers(), key: evt.Key);
// returns immediately — librdkafka batches and sends in the background
```

**`ProduceBatchAvroAsync`** — fires all produce tasks concurrently and waits for all delivery
ACKs. Returns an aggregate `Result` with per-message failure details. Use when you need delivery
confirmation before proceeding.

```csharp
var messages = events.Select(e => ((ISpecificRecord)e, (string?)e.Key)).ToList();
var result = await producer.ProduceBatchAvroAsync(messages, topic, new Headers());
int failed = result.Errors.Count;
```

> **Note:** Both methods run through the Polly resilience pipeline (circuit breaker + timeout).
> Retry is not applied to fire-and-forget or batch: for fire-and-forget there is no result to
> observe, and retrying a batch would re-attempt all N messages on any single failure.

---

## Consumer offset safety

**`enable.auto.offset.store = false`** is always set on every consumer created by this library
and cannot be overridden.

By default, Kafka marks a message as "processed" the moment it is fetched from the broker. If
your handler crashes after the fetch but before it finishes, the message offset is already
advanced and the message is silently lost.

With auto-store disabled, the library calls `consumer.StoreOffset(result)` explicitly **after**
`IMessageHandler<T>.HandleAsync` completes — whether or not the handler threw an exception.
Only then is the offset eligible for the next `CommitAsync` (handled automatically by librdkafka's
`enable.auto.commit = true`). If the process restarts before committing, any messages whose
offsets were not yet stored are re-delivered.

---

## Consumer pre-fetch buffer

librdkafka maintains an internal per-partition pre-fetch queue populated by background threads
that send `FetchRequest` batches to the broker continuously, independent of your application code.

When you call `consumer.Consume()`:
- **Queue has messages** → returns immediately (pure memory dequeue, no network round-trip)
- **Queue is empty** → blocks until the broker delivers new data

This means at high throughput consecutive `Consume()` calls drain the local buffer at memory
speed. At low throughput the queue is often empty and `Consume()` blocks on the next broker fetch.

The settings below control this buffer (advanced use only — not exposed as config options):

| librdkafka name | Confluent.Kafka property | Default | Effect |
|---|---|---|---|
| `fetch.min.bytes` | `FetchMinBytes` | `1` | Min bytes the broker accumulates before responding to a fetch request |
| `fetch.wait.max.ms` | `FetchWaitMaxMs` | `500` | Max ms the broker waits for `FetchMinBytes` before sending anyway |
| `max.partition.fetch.bytes` | `MaxPartitionFetchBytes` | `1 048 576` (1 MB) | Max bytes per partition per fetch request |
| `queued.min.messages` | `QueuedMinMessages` | `100 000` | Target number of messages librdkafka keeps pre-fetched locally |
| `queued.max.messages.kbytes` | `QueuedMaxMessagesKbytes` | `65 536` (64 MB) | Max size of the local pre-fetch queue |

---

## Running the Client App

The `OI.Messaging.Kafka.Client` console app can run in two modes.

### Normal mode

Produces a message every 5 seconds indefinitely (requires the local Kafka stack to be running):

```bash
dotnet run --project src/OI.Messaging.Kafka.Client
```

### Load test mode

Fires `<N>` messages as fast as possible, waits for all of them to be consumed, then prints a throughput summary and exits:

```bash
dotnet run --project src/OI.Messaging.Kafka.Client -- --load <N>
```

Example:

```bash
dotnet run --project OI.Messaging.Kafka/OI.Messaging.Kafka.Client -- --load 10000
```

Sample output:

```
Load test: 10000 messages → 'localdev.MBESReadingEvent'
Sending warmup messages — waiting for consumer to be ready...
Consumer ready. Starting load test...
Enqueued  : 10000 messages in 56 ms — waiting for consumer...

=== Results ===
Messages             : 10000
Enqueue              : 56 ms  (10000 msg/sec)
Consumer throughput  : 206 ms  (48544 msg/sec)
E2E (post-warmup)    : 227 ms
```

#### How it works

The test is driven by two cooperating components:

- **`EventPublisher`** (`EventPublisher.cs`) — orchestrates the run: sends a warmup message, waits for the consumer to confirm it is ready, then enqueues all `N` messages in a tight loop using `ProduceFireAndForget`. This avoids the [`LingerMs` pitfall](#the-lingerms-pitfall--sequential-await): because no `await` happens between messages, librdkafka accumulates them into a single batch and flushes it at once.

- **`LoadTestConsumeTracker`** (`LoadTestSupport.cs`) — a thread-safe countdown used by the consumer handler (`LoadTestHandler`). It treats the very first signal as the warmup acknowledgement (completing `WaitForWarmupAsync`) and ignores it for timing. Every subsequent signal decrements a counter; when it reaches zero `WaitForAllAsync` completes.

The sequence is:

1. A single warmup message is produced and the publisher blocks until the consumer signals it back via `WaitForWarmupAsync`. This guarantees the consumer group has been assigned its partition before measurement starts — without it, the first real messages could be missed if the consumer joins late.
2. `StartMeasurement()` is called, resetting the internal stopwatch on the tracker.
3. All `N` messages are enqueued with `ProduceFireAndForget` — no waiting for broker ACKs.
4. The publisher records `enqueueMs` (time to enqueue all messages into the local librdkafka buffer).
5. The publisher awaits `WaitForAllAsync`, which resolves once every message has passed through the consumer handler.

#### Metric definitions

| Metric | What it measures |
|---|---|
| **Enqueue** | Time to hand all `N` messages to librdkafka's internal send buffer. Does not include network or broker time. |
| **Consumer throughput** | `LastSignalMs − FirstSignalMs` on the tracker: the wall-clock span from the first real message being handled to the last. Reflects consumer processing speed once data starts flowing. |
| **E2E (post-warmup)** | Total elapsed time from the first `ProduceFireAndForget` call to `WaitForAllAsync` returning — the full producer-to-consumer pipeline latency, excluding warmup overhead. |

#### Configuration used

The results above were produced with the following settings (`appsettings.json`):

```json
"ProducerConfig": {
  "ClientName": "oi-messaging-kafka-client",
  "ProduceTimeout": 20,
  "LingerMs": 5
},
"ConsumerBoundsConfig": {
  "ConcurrentMessageLimit": 10
}
```

`LingerMs: 5` allows librdkafka to accumulate messages for up to 5 ms before flushing, enabling large batches. `ConcurrentMessageLimit: 10` lets the consumer process up to 10 messages concurrently.

---

## Avro Code Generation

Avro C# classes are generated from `.avsc` schema files located in `src/OI.Messaging.Kafka/Models/AvroSchemas/`.

### Install avrogen

```bash
dotnet tool install --global Apache.Avro.Tools
```

### Generate classes

```bash
avrogen -s <path/to/schema.avsc> <output-directory>
```

Example:

```bash
avrogen -s src/OI.Messaging.Kafka/Models/AvroSchemas/MBESReadingEvent.avsc src/OI.Messaging.Kafka/Models/
```

---

## Registration

Both extension methods accept strongly-typed config objects, so you control how and where configuration is sourced (JSON, environment variables, code, tests, etc.).

```csharp
// Bind from IConfiguration (typical production usage)
var connection    = config.GetRequiredSection("Kafka:Connection").Get<KafkaConnectionConfig>()!;
var producerCfg   = config.GetRequiredSection("Kafka:ProducerConfig").Get<KafkaProducerConfig>()!;
var resilience    = config.GetRequiredSection("Kafka:Resilience").Get<KafkaResilienceOptions>()!;
var consumerCfg   = config.GetRequiredSection("Kafka:ConsumerConfig").Get<KafkaConsumerConfig>()!;

services.AddOIKafkaProducer(connection, producerCfg, resilience);
services.AddOIKafkaConsumer<MyEvent, MyHandler>(connection, consumerCfg);
```

`AddOIKafkaProducer` registers:
- `ISchemaRegistryClient` (singleton `CachedSchemaRegistryClient`)
- `KafkaProducer` (inner, singleton)
- `IKafkaProducer` → `ResilientKafkaProducer` (singleton decorator)

## Usage

```csharp
public class MyService(IKafkaProducer producer)
{
    public async Task SendAvro(MBESReadingEvent evt) =>
        await producer.ProduceAvroConfirmedAsync(evt, "my-topic", new Headers());

    public async Task SendRaw(byte[] payload) =>
        await producer.ProduceAsync(payload, "my-topic", new Headers());
}
```
