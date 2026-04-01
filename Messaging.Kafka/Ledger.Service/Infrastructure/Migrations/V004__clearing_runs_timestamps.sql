-- V004: Switch to timestamp high-watermarks, drop clearing_run_id from ledger
ALTER TABLE journal_entries DROP COLUMN IF EXISTS clearing_run_id;
ALTER TABLE installment_receivables DROP COLUMN IF EXISTS clearing_run_id;

-- Add previous_cutoff_timestamp to clearing_runs to define the lower bound of the run
ALTER TABLE clearing_runs ADD COLUMN IF NOT EXISTS previous_cutoff_timestamp TIMESTAMPTZ NOT NULL DEFAULT '1970-01-01T00:00:00Z';
