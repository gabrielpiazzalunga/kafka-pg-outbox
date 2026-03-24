CREATE TABLE IF NOT EXISTS outbox (
    id UUID PRIMARY KEY,
    aggregate_type VARCHAR(255) NOT NULL,
    aggregate_id VARCHAR(255) NOT NULL,
    type VARCHAR(255) NOT NULL,
    payload BYTEA NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS settlement_files (
    file_id VARCHAR(255) PRIMARY KEY,
    status VARCHAR(50) NOT NULL DEFAULT 'PENDING',
    created_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMPTZ
);

-- UNLOGGED: skips WAL writes for faster inserts (safe because this is
-- rebuild-able state tracking, not business data. If Postgres crashes,
-- we re-derive from the outbox table or re-process the file).
-- No FK: avoids per-row FK lookup overhead during COPY bulk import.
CREATE UNLOGGED TABLE IF NOT EXISTS file_offsets (
    file_id VARCHAR(255) NOT NULL,
    record_index BIGINT NOT NULL,
    processed_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT pk_file_offsets PRIMARY KEY (file_id, record_index)
);
