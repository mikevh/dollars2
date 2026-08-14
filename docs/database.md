# Database Schema

## Conventions

- Primary keys: `int` identity auto-increment
- Money columns: `decimal(18,2)`
- All tables have `CreatedAt datetime2` and `UpdatedAt datetime2` — except `AccountBalances`, which
  is append-only and uses `CreatedOn` / `UpdatedOn`
- Sort order: `int` starting at 0
- Raw SQL migration scripts, numbered, run manually, tracked via migrations table
- Every constraint is named explicitly with the `CONSTRAINT <name> ...` form — never the inline
  `PRIMARY KEY` / `REFERENCES` / `UNIQUE` / bare `DEFAULT` column shorthand, which makes MSSQL
  generate a per-database name (`DF__BudgetGro__IsInc__3E52440B`) that no later migration can
  reference. `ConstraintNamingTests` fails the build if a system-named constraint appears.

| Object | Pattern | Example |
|--------|---------|---------|
| Primary key | `PK_<Table>` | `PK_LineItems` |
| Foreign key | `FK_<Table>_<ReferencedTable>` | `FK_LineItems_BudgetGroups` |
| Foreign key (2+ to the same table) | `FK_<Table>_<Column>` | `FK_LineItems_PreviousLineItem` |
| Unique constraint | `UQ_<Table>_<Columns>` | `UQ_Budgets_UserId_Year_Month` |
| Default | `DF_<Table>_<Column>` | `DF_LineItems_IsIncome` |
| Check | `CK_<Table>_<Rule>` | `CK_Budgets_MonthRange` |
| Index / unique index | `IX_`/`UX_<Table>_<Columns>` | `IX_SyncLog_AccountId_SyncedAt` |

Migration `017_name_existing_constraints` renamed every constraint that predated this convention, so
migrated and freshly-created databases carry identical names.

## Tables

### Users

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| Email | nvarchar(256) | unique |
| RegistrationKey | nvarchar(200) | null. Set (via direct DB update) by an admin to enroll or re-enroll a passkey; blanked back to null once registration completes (migration 023) |
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

### PasskeyCredentials

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| UserId | int | FK → Users |
| CredentialId | varbinary(900) | unique. WebAuthn credential ID; sized to fit within MSSQL's 900-byte index key limit |
| PublicKey | varbinary(max) | |
| AttestationObject | varbinary(max) | |
| ClientDataJson | varbinary(max) | |
| SignCount | bigint | replay-protection counter; framework type is `uint`, stored as bigint to avoid overflow |
| Transports | nvarchar(200) | null |
| IsUserVerified | bit | |
| IsBackupEligible | bit | |
| IsBackedUp | bit | |
| Name | nvarchar(256) | null. User-friendly passkey name |
| CreatedAt | datetime2 | framework-supplied value, not a default |
| UpdatedAt | datetime2 | |

Backs ASP.NET Core Identity's `IUserPasskeyStore<TUser>` (migration 023). One row per registered
WebAuthn credential; a lost-passkey re-registration deletes the user's prior rows rather than
accumulating them.

### Accounts

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| UserId | int | FK → Users |
| Name | nvarchar(256) | |
| SourceType | nvarchar(50) | "Plaid", "SimpleFIN", "Manual" |
| ConnectionDetailsJson | nvarchar(max) | provider-specific config |
| IncludeInBudget | bit | NOT NULL, default 1. When 0, the account's transactions are hidden from the budget transaction pane (see `accounts.md`) |
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
| SortOrder | int | starts at 0 |
| CreatedAt | datetime2 | |
| UpdatedAt | datetime2 | |

### LineItems

| Column | Type | Notes |
|--------|------|-------|
| Id | int | PK, identity |
| GroupId | int | FK → BudgetGroups. Presentation/grouping reference only — ownership and aggregation queries use BudgetId directly |
| BudgetId | int | FK → Budgets (migration 021/022). Direct link so ownership/aggregation queries don't have to route through BudgetGroups |
| Name | nvarchar(256) | |
| PlannedAmount | decimal(18,2) | |
| IsIncome | bit | moved from BudgetGroups (migration 019) — income-ness is a line item property; a budget always keeps at least one |
| SortOrder | int | starts at 0 |
| Notes | nvarchar(max) | NOT NULL, default `''` (migration 018) |
| PreviousLineItemId | int | FK → LineItems (self), null. Links this month's line item to its prior-month counterpart — this chain is what rollover walks |
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
| Payee | nvarchar(500) | NOT NULL (migration 014) |
| Memo | nvarchar(500) | NOT NULL (migration 014) |
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
- Users → PasskeyCredentials (1:many)
- Budgets → BudgetGroups (1:many)
- BudgetGroups → LineItems (1:many)
- LineItems → LineItems (self, via `PreviousLineItemId` — the month-over-month rollover chain)
- Accounts → Transactions (1:many)
- Accounts → SyncLog (1:many)
- Accounts → AccountBalances (1:many)
- Transactions → TransactionAssignments (1:many)
- LineItems → TransactionAssignments (1:many)
