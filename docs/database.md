# Database Schema

## Conventions

- Primary keys: `int` identity auto-increment
- Money columns: `decimal(18,2)`
- All tables have `CreatedAt datetime2` and `UpdatedAt datetime2`
- Sort order: `int` starting at 0
- Raw SQL migration scripts, numbered, run manually, tracked via migrations table

## Tables

### Users

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| Email | nvarchar(256) | unique |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

### RefreshTokens

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| UserId | int | FK → Users |
| Token | nvarchar(500) | |
| ExpiresAt | datetime2 | |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

### Accounts

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| UserId | int | FK → Users |
| Name | nvarchar(256) | |
| SourceType | nvarchar(50) | "Plaid", "SimpleFIN", "Manual" |
| ConnectionDetailsJson | nvarchar(max) | provider-specific config |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

### Budgets

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| UserId | int | FK → Users |
| Year | int | |
| Month | int | 1-12 |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

Unique constraint: (UserId, Year, Month)

### BudgetGroups

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| BudgetId | int | FK → Budgets |
| Name | nvarchar(256) | |
| IsIncome | bit | true for the income group |
| SortOrder | int | starts at 0 |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

### LineItems

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| GroupId | int | FK → BudgetGroups |
| Name | nvarchar(256) | |
| PlannedAmount | decimal(18,2) | |
| SortOrder | int | starts at 0 |
| Notes | nvarchar(max) | |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

### Transactions

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| UserId | int | FK → Users |
| AccountId | int | FK → Accounts, null for manual |
| ProviderTransactionId | nvarchar(500) | null for manual |
| Date | date | |
| Description | nvarchar(500) | |
| Amount | decimal(18,2) | positive = income, negative = expense |
| Notes | nvarchar(max) | |
| IsDeleted | bit | soft-delete flag |
| IsPending | bit | |
| IsManual | bit | |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

Unique constraint: (AccountId, ProviderTransactionId) where ProviderTransactionId is not null

### TransactionAssignments

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| TransactionId | int | FK → Transactions |
| LineItemId | int | FK → LineItems |
| Amount | decimal(18,2) | split amount (must total to transaction amount) |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

Unique constraint: (TransactionId, LineItemId)

### SyncLog

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| AccountId | int | FK → Accounts |
| SyncedAt | datetime2 | |
| Status | nvarchar(50) | "Success", "Failed" |
| ErrorMessage | nvarchar(max) | null on success |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

One entry per sync attempt per account (history, not just latest).

### AccountBalances

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| AccountId | int | FK → Accounts |
| Balance | decimal(18,2) | provider-reported current balance |
| CreatedOn | datetime2 | when this balance was recorded (default SYSUTCDATETIME) |
| UpdatedOn | datetime2 | equals CreatedOn on insert (append-only) |

A new row is appended on every successful sync where the provider reports a parseable balance, so the
table is a time-series of balances per account (history, not just latest). Indexed on `(AccountId,
CreatedOn DESC)`.

## Relationships

- Users → Budgets (1:many)
- Users → Accounts (1:many)
- Users → RefreshTokens (1:many)
- Budgets → BudgetGroups (1:many)
- BudgetGroups → LineItems (1:many)
- Accounts → Transactions (1:many)
- Accounts → SyncLog (1:many)
- Accounts → AccountBalances (1:many)
- Transactions → TransactionAssignments (1:many)
- LineItems → TransactionAssignments (1:many)
