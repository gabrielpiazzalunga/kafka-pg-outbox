# PostgreSQL Partitioning Strategy with pg_partman

## Problem

The Feed Ingestion pipeline inserts millions of rows per file into both the `outbox` and `file_offsets` tables. On AWS RDS PostgreSQL (which does not support TimescaleDB), this causes:

- **Table bloat** — MVCC dead tuples accumulate after Debezium consumes and rows are deleted.
- **VACUUM pressure** — Autovacuum struggles to keep up with high-churn tables.
- **Index bloat** — B-tree indexes grow and never shrink, even after rows are removed.
- **Slow DELETEs** — Cleaning up 20M rows with `DELETE` is extremely expensive.

## Solution: Native Partitioning + pg_partman

AWS RDS supports `pg_partman`, which automates partition creation, retention, and cleanup. Partitions are dropped instantly (`DROP TABLE`) with zero VACUUM overhead.

### Enable pg_partman on RDS

```sql
CREATE EXTENSION IF NOT EXISTS pg_partman;
```

> [!NOTE]
> On RDS, pg_partman's background worker (`pg_partman_bgw`) must be added to `shared_preload_libraries` via the RDS Parameter Group. This enables automatic partition maintenance without external cron.

### Outbox Table (Partitioned by Hour)

```sql
CREATE TABLE outbox (
    id UUID NOT NULL,
    aggregate_type VARCHAR(255) NOT NULL,
    aggregate_id VARCHAR(255) NOT NULL,
    type VARCHAR(255) NOT NULL,
    payload BYTEA NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
) PARTITION BY RANGE (created_at);

-- Register with pg_partman
SELECT partman.create_parent(
    p_parent_table  := 'public.outbox',
    p_control       := 'created_at',
    p_interval      := '1 hour',
    p_premake       := 3
);

-- Auto-drop partitions older than 24 hours
UPDATE partman.part_config
SET retention            = '24 hours',
    retention_keep_table = false
WHERE parent_table = 'public.outbox';
```

**How it works with Debezium:** Debezium captures inserts via WAL/CDC regardless of partitioning. Once a partition's data has been consumed (within the 24h window), pg_partman drops the entire partition table — instant, no VACUUM.

### File Offsets Table (Partitioned by Hour)

```sql
CREATE TABLE file_offsets (
    file_id VARCHAR(255) NOT NULL,
    record_index BIGINT NOT NULL,
    processed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT pk_file_offsets PRIMARY KEY (file_id, record_index, processed_at)
) PARTITION BY RANGE (processed_at);

SELECT partman.create_parent(
    p_parent_table  := 'public.file_offsets',
    p_control       := 'processed_at',
    p_interval      := '1 hour',
    p_premake       := 3
);

UPDATE partman.part_config
SET retention            = '48 hours',
    retention_keep_table = false
WHERE parent_table = 'public.file_offsets';
```

> [!IMPORTANT]
> The `processed_at` column must be part of the PRIMARY KEY when using declarative partitioning, because PostgreSQL requires the partition key in unique constraints.

### RDS Parameter Group Configuration

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `shared_preload_libraries` | `pg_partman_bgw` | Enables automatic maintenance |
| `pg_partman_bgw.interval` | `3600` | Run maintenance every hour (seconds) |
| `pg_partman_bgw.dbname` | `feed_ingestion` | Target database |

### Maintenance Verification

To manually trigger maintenance or verify partition state:

```sql
-- Run maintenance manually
SELECT partman.run_maintenance();

-- View current partitions
SELECT * FROM partman.show_partitions('public.outbox');

-- Check partition config
SELECT * FROM partman.part_config;
```
