-- One-time backfill of the Migrations tracking table.
--
-- Context: migrations 000-005 and 011-016 originally guarded on object existence
-- (sys.tables / sys.columns / sys.indexes) and never recorded a Migrations row, so a
-- database migrated before the scripts were normalized only has rows for 006-010.
-- The normalized scripts skip their DDL when their Migrations row is present, so without
-- these rows they would try to re-run their CREATE/ALTER against objects that already exist.
--
-- Run this ONCE against a database that has already been migrated through 016, BEFORE
-- applying the normalized scripts (scripts/migrate.ps1). It only inserts the rows that are
-- missing, so re-running it is harmless. Do NOT run it against a partially-migrated database:
-- it would record scripts as applied whose objects do not actually exist yet.

INSERT INTO Migrations (ScriptName)
SELECT v
FROM (VALUES
    ('000_create_migrations_table'),
    ('001_create_users'),
    ('002_create_refresh_tokens'),
    ('003_create_budgets'),
    ('004_create_budget_groups'),
    ('005_create_line_items'),
    ('006_create_accounts'),
    ('007_create_transactions'),
    ('008_create_transaction_assignments'),
    ('009_add_unique_transaction_assignment'),
    ('010_allow_split_assignments'),
    ('011_add_previous_line_item_id'),
    ('012_add_refresh_token_index'),
    ('013_create_sync_log'),
    ('014_add_transaction_payee_memo'),
    ('015_create_account_balances'),
    ('016_add_account_include_in_budget')
) t(v)
WHERE NOT EXISTS (SELECT 1 FROM Migrations WHERE ScriptName = t.v);
