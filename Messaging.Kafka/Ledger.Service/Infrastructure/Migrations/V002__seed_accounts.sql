-- =====================================================
-- SEED ACCOUNTS
-- =====================================================
INSERT INTO accounts (account_id, account_code, account_name, account_type, normal_balance) VALUES
(1, '1000', 'auth_holding', 'asset', 'debit'),
(2, '1100', 'charge_captured', 'asset', 'debit'),
(3, '1200', 'network_receivable', 'asset', 'debit'),
(4, '1400', 'merchant_payable', 'liability', 'credit'),
(5, '2000', 'mdr_revenue', 'revenue', 'credit')
ON CONFLICT DO NOTHING;
