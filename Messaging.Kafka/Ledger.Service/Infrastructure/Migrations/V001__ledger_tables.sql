-- =====================================================
-- ACCOUNTS: Chart of accounts
-- =====================================================
CREATE TABLE accounts (
    account_id      SMALLINT PRIMARY KEY,
    account_code    TEXT NOT NULL UNIQUE,      -- '1000', '1201', etc.
    account_name    TEXT NOT NULL,
    account_type    TEXT NOT NULL,              -- 'asset', 'liability', 'revenue', 'expense', 'external'
    normal_balance  TEXT NOT NULL,              -- 'debit' or 'credit'
    description     TEXT,
    is_installment  BOOLEAN DEFAULT FALSE,     -- marks accounts that fan-out per parcela
    created_at      TIMESTAMPTZ DEFAULT NOW()
);

-- =====================================================
-- JOURNAL_ENTRIES: One per financial event
-- =====================================================
CREATE TABLE journal_entries (
    entry_id            BIGSERIAL PRIMARY KEY,
    idempotency_key     TEXT NOT NULL UNIQUE,   -- prevents duplicate postings
    event_type          TEXT NOT NULL,           -- 'authorization', 'capture', etc.
    event_timestamp     TIMESTAMPTZ NOT NULL,    -- when the business event occurred
    posted_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    transaction_id      UUID NOT NULL,           
    merchant_id         TEXT NOT NULL,
    merchant_cnpj       TEXT,                    

    network             TEXT NOT NULL,            -- 'visa', 'mastercard', 'elo'
    arn                 TEXT,                      -- Visa: 23-digit ARN
    network_ref_id      TEXT,                      -- Mastercard reference
    authorization_code  TEXT,                      

    installment_number  SMALLINT,                 
    installment_total   SMALLINT,                 
    installment_plan_id UUID,                     

    clearing_run_id     INT,                      
    clearing_date       DATE,

    gross_amount        NUMERIC(18,2) NOT NULL,   
    currency            TEXT NOT NULL DEFAULT 'BRL',

    source_system       TEXT,                      
    correlation_id      TEXT                       
);

CREATE INDEX idx_je_transaction  ON journal_entries (transaction_id);
CREATE INDEX idx_je_merchant     ON journal_entries (merchant_id, posted_at);
CREATE INDEX idx_je_arn          ON journal_entries (arn) WHERE arn IS NOT NULL;
CREATE INDEX idx_je_network_ref  ON journal_entries (network_ref_id) WHERE network_ref_id IS NOT NULL;
CREATE INDEX idx_je_clearing     ON journal_entries (clearing_run_id) WHERE clearing_run_id IS NOT NULL;
CREATE INDEX idx_je_installment  ON journal_entries (installment_plan_id, installment_number) WHERE installment_plan_id IS NOT NULL;

-- =====================================================
-- BOOK_ENTRIES: Debit/Credit legs of each journal entry
-- =====================================================
CREATE TABLE book_entries (
    book_entry_id   BIGSERIAL PRIMARY KEY,
    entry_id        BIGINT NOT NULL REFERENCES journal_entries(entry_id),
    account_id      SMALLINT NOT NULL REFERENCES accounts(account_id),
    entry_type      TEXT NOT NULL,              -- 'debit' or 'credit'
    amount          NUMERIC(18,2) NOT NULL,     -- always positive
    memo            TEXT,

    CONSTRAINT chk_entry_type CHECK (entry_type IN ('debit', 'credit')),
    CONSTRAINT chk_positive_amount CHECK (amount > 0)
);

CREATE INDEX idx_be_entry   ON book_entries (entry_id);
CREATE INDEX idx_be_account ON book_entries (account_id, entry_id);

-- =====================================================
-- INSTALLMENT_PLANS: High-level plan for parcelado
-- =====================================================
CREATE TABLE installment_plans (
    plan_id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    transaction_id      UUID NOT NULL,           -- origin txn
    merchant_id         TEXT NOT NULL,
    merchant_cnpj       TEXT NOT NULL,
    network             TEXT NOT NULL,
    card_bin            TEXT NOT NULL,            
    total_amount        NUMERIC(18,2) NOT NULL,  
    installment_count   SMALLINT NOT NULL,        
    installment_amount  NUMERIC(18,2) NOT NULL,  
    currency            TEXT NOT NULL DEFAULT 'BRL',
    authorization_code  TEXT NOT NULL,
    captured_at         TIMESTAMPTZ,
    created_at          TIMESTAMPTZ DEFAULT NOW()
);

-- =====================================================
-- INSTALLMENT_RECEIVABLES: The actual monthly agenda
-- =====================================================
CREATE TABLE installment_receivables (
    receivable_id       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    plan_id             UUID NOT NULL REFERENCES installment_plans(plan_id),
    installment_number  SMALLINT NOT NULL,        
    amount              NUMERIC(18,2) NOT NULL,   
    maturity_date       DATE NOT NULL,             

    arn                 TEXT,                      
    network_ref_id      TEXT,

    clearing_run_id     INT,
    clearing_date       DATE,
    clearing_status     TEXT NOT NULL DEFAULT 'pending',

    registradora        TEXT,                      
    registration_id     TEXT,                      
    registration_status TEXT DEFAULT 'pending',    
    registered_at       TIMESTAMPTZ,

    anticipated         BOOLEAN DEFAULT FALSE,
    anticipation_date   DATE,
    anticipation_rate   NUMERIC(6,4),              
    anticipated_amount  NUMERIC(18,2),             

    UNIQUE (plan_id, installment_number)
);

CREATE INDEX idx_ir_plan       ON installment_receivables (plan_id);
CREATE INDEX idx_ir_maturity   ON installment_receivables (maturity_date, clearing_status);
CREATE INDEX idx_ir_clearing   ON installment_receivables (clearing_run_id) WHERE clearing_run_id IS NOT NULL;
CREATE INDEX idx_ir_registradora ON installment_receivables (registradora, registration_status);
