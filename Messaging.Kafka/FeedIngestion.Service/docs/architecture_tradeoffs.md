# Architecture Trade-offs: Feed Ingestion

This document analyzes the trade-offs between two ingestion architectures for high-volume settlement feeds (like Visa or Mastercard): the **PostgreSQL Transactional Outbox** (Alternative 1) and the **Redis + Direct Kafka** approach (Alternative 2).

## Performance Comparison

| Metric | PostgreSQL Outbox (`FeedIngestionJob`) | Redis + Kafka (`FeedIngestionRedisJob`) |
|--------|----------------------------------------|-----------------------------------------|
| **Throughput** | ~15,000 records/sec | **~90,000+ records/sec** (6x faster) |
| **I/O Bottleneck** | Disk I/O (WAL + heap + indexes for two tables) | Network I/O (Kafka `ProduceAsync` + Redis `SET`) |
| **Latency to Kafka** | Higher (Debezium polling delay, typically 100-500ms) | **Instant** (Available immediately after batch ack) |
| **Per-batch overhead** | `COPY` 1,000 rows to DB = ~220ms | Stream 500 messages to Kafka memory log = ~40ms |

### Why Redis + Kafka is Faster
The bottleneck in the Outbox pattern is PostgreSQL disk I/O. For every batch of events, PostgreSQL must write data to the Write-Ahead Log (WAL), update table heaps, update B-tree indexes, and perform an `fsync` on commit. Kafka's append-only log combined with Redis's in-memory key-value store eliminates this heavy I/O cost almost entirely.

---

## Delivery Guarantees & Failure Scenarios

### Alternative 1: PostgreSQL Outbox (Exactly-Once)
Because writing the offset state (`file_offsets`) and the events (`outbox_events`) happens in the same atomic database transaction, the Outbox pattern provides a strict **exactly-once** guarantee for event production. There is no scenario where an event is published multiple times due to an ingestion crash.

### Alternative 2: Redis + Direct Kafka (At-Least-Once)
Because Kafka and Redis are completely independent systems without distributed transactions, this approach only guarantees **at-least-once** delivery. The gap between updating Kafka and updating Redis introduces a window for duplicate events.

**Duplicate Risks in Redis + Kafka:**
1. **Process Crash (Batch Duplication):** If the worker process crashes after Kafka acknowledges the batch but *before* the Redis offset is updated, the worker will resume from the old Redis offset on restart.
   * **Impact:** Exactly 1 batch (e.g., 500 records) of duplicates is produced.
   * **Likelihood:** Uncommon.

2. **Redis Data Loss (Catastrophic Duplication):** If the Redis instance undergoes an ungraceful restart without persistence (e.g., AOF disabled or corrupted), the offset key for the file is lost. When the worker resumes, it cannot find the offset in Redis.
   * **Impact:** The worker reads `nil`, treats the offset as `0`, and **re-processes the entire file from line 1**. For a 20-million-record file, this results in 20 million duplicate events.
   * **Likelihood:** Very rare (assuming Redis is configured with AOF/persistence), but highly impactful.

---

## The Role of Idempotent Consumers

In financial reconciliation systems, generating duplicates naturally sounds dangerous. However, the Redis + Kafka architecture becomes perfectly safe **if and only if all downstream consumers are strictly idempotent**.

### Natural Idempotency Keys
In settlement data, we already have natural business keys:
* **Visa:** Acquirer Reference Number (ARN)
* **Mastercard:** Trace ID

These keys uniquely identify a transaction globally. Therefore, we do not need to generate synthetic, deterministic IDs (like `hash(file_id + record_index)`). We can simply use the ARN.

### Safe Consumer Pattern (Upsert)
A downstream settlement matching service must defensively handle incoming messages:
```sql
-- The consumer uses an UPSERT (ON CONFLICT) rather than a blind INSERT
INSERT INTO matched_settlements (arn, amount, file_id, processing_date)
VALUES (@Arn, @Amount, @FileId, @ProcessingDate)
ON CONFLICT (arn) DO NOTHING;  -- Or DO UPDATE for idempotent backfill
```

**Conclusion:** As long as consumers deduplicate using business keys via upserts, receiving the same settlement event 1 time or 100 times results in the exact same financial state. The duplicates generated during a Redis+Kafka crash scenario (even millions of them) degrade into a temporary performance penalty rather than a financial correctness bug.

## Final Recommendation
* Use **PostgreSQL Outbox** if downstream consumers cannot be trusted to be idempotent, or if you strictly require an SQL-queryable audit log of every published event.
* Use **Redis + Direct Kafka** (the 6x faster approach) if throughput is paramount, latency must be low, and consumers are appropriately defensive (e.g., relying on `acquirer_reference_number` constraints). For settlement reconciliation, this is highly recommended.
