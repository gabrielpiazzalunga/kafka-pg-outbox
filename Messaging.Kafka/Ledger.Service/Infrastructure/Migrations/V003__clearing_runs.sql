-- V003: Clearing Runs Support
CREATE TABLE IF NOT EXISTS clearing_runs (
    id                  SERIAL PRIMARY KEY,
    network             TEXT NOT NULL,          -- 'visa', 'mastercard', 'elo'
    clearing_date       DATE NOT NULL,
    status              TEXT NOT NULL,          -- 'LOCKING', 'DRAINING', 'VALIDATING', 'GENERATING', 'COMPLETED', 'FAILED'
    cutoff_timestamp    TIMESTAMPTZ NOT NULL,
    expected_count      BIGINT,
    actual_count        BIGINT,
    expected_amount     NUMERIC(18,2),
    actual_amount       NUMERIC(18,2),
    spark_app_id        TEXT,
    s3_file_path        TEXT,
    error_message       TEXT,
    retry_count         INT DEFAULT 0,
    created_at          TIMESTAMPTZ DEFAULT NOW(),
    completed_at        TIMESTAMPTZ,
    UNIQUE (network, clearing_date)
);

-- Add the clearing run lock to the ledger safely
ALTER TABLE journal_entries ADD COLUMN IF NOT EXISTS clearing_run_id INT REFERENCES clearing_runs(id);
CREATE INDEX IF NOT EXISTS idx_ledger_clearing_run ON journal_entries (clearing_run_id) WHERE clearing_run_id IS NOT NULL;
