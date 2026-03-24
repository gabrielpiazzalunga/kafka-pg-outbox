# Alternative 2: Redis Offset + Direct Kafka — Failure Scenarios

This document details the failure modes and delivery guarantees of the **Redis Offset + Direct Kafka** ingestion alternative (`FeedIngestionRedisJob`), as opposed to the PostgreSQL Transactional Outbox approach (`FeedIngestionJob`).

## Delivery Guarantee: At-Least-Once

Because two independent systems (Kafka and Redis) cannot be committed atomically, the ordering of operations within each batch determines the duplicate window:

```
┌─────────────────────────────────────────────────────────────┐
│  Per-Batch Pipeline (500 records)                           │
│                                                             │
│  1. Parse 500 lines + serialize Protobuf          (in-mem)  │
│  2. Produce 500 messages to Kafka (Acks=All)      (Kafka)   │
│  3. Update Redis offset to max(record_index)      (Redis)   │
│                                                             │
│  ──── Crash between step 2 and step 3 = DUPLICATES ────     │
└─────────────────────────────────────────────────────────────┘
```

---

## Failure Scenario Matrix

| # | Failure Point | Kafka State | Redis State | On Resume | Duplicates? |
|---|---------------|-------------|-------------|-----------|-------------|
| 1 | **Before** Kafka produce | No messages sent | Unchanged | Re-processes same batch | ❌ None |
| 2 | **During** Kafka produce (partial) | Some messages delivered | Unchanged | Re-produces entire batch | ⚠️ Partial (messages already acked are re-sent) |
| 3 | **After** Kafka produce, **before** Redis SET | All 500 messages delivered | Old offset | Re-produces entire batch | ⚠️ Full batch (500 messages duplicated) |
| 4 | **After** Redis SET | All 500 messages delivered | Updated | Moves to next batch | ❌ None |
| 5 | **Redis down** | Produce halts (no offset check possible) | N/A | Retries until Redis recovers | ❌ None (stalls) |
| 6 | **Kafka down** | ProduceAsync throws/times out | Unchanged | Retries same batch | ❌ None |
| 7 | **Redis data loss** (restart without AOF) | Already published | Offset lost (resets to 0) | Re-processes entire file | ⚠️ Full file re-published |

---

## Scenario 1: Process Crash After Kafka Publish, Before Redis Update

This is the **most common** duplicate scenario and the one operators must plan for.

```
Timeline:
──────────────────────────────────────────────────────────────
t1: Consumer reads batch (records 5001-5500) from Channel
t2: Kafka producer.ProduceAsync() succeeds → 500 messages in topic ✅
t3: ─── PROCESS CRASHES (SIGKILL / OOM / host failure) ───
t4: Redis still holds offset = 5000 (never updated)

On restart:
t5: Worker reads Redis offset → 5000
t6: Producer fast-forwards file to line 5001
t7: Consumer re-processes records 5001-5500 → 500 DUPLICATE messages in Kafka
t8: Redis updated to 5500
```

**Impact**: 500 duplicate messages in the Kafka topic.
**Mitigation**: Downstream consumers MUST deduplicate using:
- `acquirer_reference_number` (Visa) or `trace_id` (Mastercard) as a natural idempotency key
- Or a deterministic message ID derived from `file_id + record_index`

---

## Scenario 2: Redis Data Loss

If Redis restarts **without** AOF/RDB persistence, all offset keys are lost.

```
Timeline:
──────────────────────────────────────────────────────────────
t1: Worker has processed 200,000 records, Redis offset = 200000
t2: Redis pod crashes and restarts (no persistence)
t3: Redis offset key is gone → GET returns nil → worker treats as 0
t4: Worker re-processes the entire file from line 1
t5: 200,000 duplicate messages published to Kafka
```

**Mitigation**:
- Always deploy Redis with `--appendonly yes` (AOF enabled) — already configured in `redis.yaml`
- For production: use Redis with RDB snapshots + AOF, or AWS ElastiCache with Multi-AZ

---

## Scenario 3: Kafka Unavailability

```
Timeline:
──────────────────────────────────────────────────────────────
t1: Consumer parses batch successfully
t2: Kafka broker is unreachable → ProduceAsync throws TimeoutException
t3: Batch fails, Redis is NOT updated
t4: Worker retries the same batch on next loop iteration
```

**Impact**: Zero duplicates. Processing stalls until Kafka recovers.

---

## Comparison: Outbox vs Redis+Kafka

| Property | PostgreSQL Outbox | Redis + Direct Kafka |
|----------|-------------------|----------------------|
| **Delivery guarantee** | Exactly-once (to outbox) | At-least-once |
| **Throughput** | ~15,000 rec/sec (COPY) | Expected higher (no DB write for events) |
| **Duplicate risk** | None (atomic Tx) | On crash between Kafka ack and Redis update |
| **Infrastructure** | PostgreSQL + Debezium | Redis + Kafka (no Debezium needed) |
| **Downstream requirement** | Simple consumers | Idempotent consumers required |
| **Data audit trail** | Full (outbox rows in DB) | None (events only in Kafka) |
| **Recovery complexity** | Simple (query MAX offset) | Simple (query Redis key) |

---

## Recommended Downstream Deduplication Pattern

```csharp
// In the settlement matching consumer:
var key = $"{event.FileId}:{event.RecordIndex}";

if (await idempotencyStore.ExistsAsync(key))
{
    logger.LogWarning("Duplicate event skipped: {Key}", key);
    return; // Already processed
}

await processSettlement(event);
await idempotencyStore.SetAsync(key, ttl: TimeSpan.FromDays(7));
```

The idempotency store can be Redis, PostgreSQL, or any key-value store. The TTL should exceed the maximum expected re-processing window (typically 24-48 hours for settlement files).
