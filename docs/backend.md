# Backend

## Stack

- .NET 10, ASP.NET Core Web API with controllers
- Dapper (ORM)
- MSSQL
- Built-in DI, built-in Microsoft logging

## Project Structure

- Single project for v1
- No unit or integration tests for v1

## API Design

- REST
- No API versioning for v1
- Envelope response format (both fields always present):
  - Success: `{ "data": { ... }, "error": null }`
  - Failure: `{ "data": null, "error": { "message": "...", "code": "..." } }`
- CORS: allow configured frontend URL from appsettings.json
- Dates and times are split by type, and the type determines the wire format:
  - **Instants** (`DateTime` — `CreatedAt`, `UpdatedAt`, `SyncedAt`, `ExpiresAt`, …) are stored and
    returned as UTC, and always serialize with a `Z` marker (`2026-07-20T08:00:00Z`) via the global
    `UtcDateTimeConverter`. An unmarked date-time string is parsed by the browser as *local* time, so
    omitting the marker silently shifts every instant by the viewer's UTC offset.
  - **Calendar dates** (`DateOnly` — `Transactions.Date`, a SQL `date` column) serialize bare
    (`2026-07-22`). They have no instant to convert; marking one as UTC would shift it to the previous
    day for every user west of UTC.
  - Because calendar dates are `DateOnly`, every remaining `DateTime` in the API is by definition an
    instant, which is what makes the global converter safe. Keep it that way: a new calendar date is
    `DateOnly`, never a `DateTime` with a zeroed time.
  - Dapper reads a SQL `date` into `DateOnly` via `DateOnlyTypeHandler`, registered by a module
    initializer in `Data/DateOnlyTypeHandler.cs` (writes bind natively; reads do not).

## Authentication

- Email-only login for v1 (no password)
- JWT with 30-day expiration
- Refresh tokens
- JWT secret in appsettings.json
- Users created directly in the database

## Validation

- Input validation: data annotations on request DTOs
- Business rule validation: return error results from service methods (not exceptions)

## Database

- Raw SQL migration scripts, numbered: `001_create_users.sql`, `002_create_budgets.sql`, etc.
- Each script guards on its own `Migrations` row (`IF NOT EXISTS (SELECT * FROM Migrations
  WHERE ScriptName = 'NNN_...') BEGIN <DDL>; INSERT INTO Migrations (ScriptName) VALUES ('NNN_...'); END`),
  so scripts are pure no-ops once applied and never probe object existence
- Run manually via `scripts/migrate.ps1` (PowerShell + `sqlcmd`), which applies every
  `Migrations/*.sql` in filename order; re-runnable, a fully-migrated DB produces no changes
- For a DB migrated before the scripts were normalized (rows only for 006–010), run
  `scripts/backfill_migrations.sql` **once** before `migrate.ps1`
- Migrations tracking table (`Migrations`) records each applied script by `ScriptName`
- Connection string in appsettings.json

## Logging

- Serilog, configured in one place: `Logging/SerilogConfiguration.ConfigureDollars2Logging`
- Sinks:
  - **Console** — always on
  - **Rolling file** — `logs/dollars2-<date>.log`, daily rollover, 14 files retained
  - **Elasticsearch** — added only when `Elasticsearch:Uri` is configured; ships logs to the
    `logs-dollars2` data stream. Absent in local dev / tests / CI, so no Elasticsearch is required
    to run or test the app. An unreachable Elasticsearch never takes the app down — console + file
    logging continue regardless.
- In the home-server deployment (`docker-compose.yml`), `Elasticsearch` and `Kibana` run as services
  on the same host. The backend points at `http://elasticsearch:9200`; browse logs in Kibana on port
  `5601`. Elasticsearch runs single-node with security disabled — the stack is only reachable on the
  LAN / Tailscale network, so TLS and auth are out of scope.

## Bank Sync

- `IHostedService` with a timer running every hour
- Checks each account's configured sync interval to determine if a sync is needed
- Default sync interval: 12 hours (configurable per data source)
- Manual sync endpoint for on-demand sync
- Only imports posted transactions; pending transactions shown separately
- Deduplication via provider transaction ID
- Re-synced soft-deleted transactions: set isDeleted back to false
- On sync failure: log and wait for next scheduled run (Polly retries in future versions)
- Sync status exposed to frontend (last sync time, status per account)
- On each successful account sync, the provider-reported current balance is appended to
  `AccountBalances` (in the same per-account transaction as the transaction upserts and sync-log
  entry), building a balance time-series. A null/unparseable balance records no row.

## Provider Abstraction

- `IBankSyncProvider` interface implemented by both providers
- **Plaid:** Going.Plaid SDK
- **SimpleFIN:** raw HTTP calls

## API Endpoints

### Auth
- `POST /api/auth/login` — email in, JWT + refresh token out
- `POST /api/auth/refresh` — refresh token in, new JWT out

### Budgets
- `GET /api/budgets/{year}/{month}` — get a month's budget (groups, line items, calculated remaining with rollover)
- `POST /api/budgets` — create a new month's budget (copies from prior month)

### Groups
- `POST /api/budgets/{budgetId}/groups` — create a group
- `PUT /api/groups/{id}` — rename a group
- `DELETE /api/groups/{id}` — delete (blocked if line items exist)
- `PUT /api/groups/reorder` — update sort order

### Line Items
- `POST /api/groups/{groupId}/line-items` — create
- `PUT /api/line-items/{id}` — update planned amount, rename
- `DELETE /api/line-items/{id}` — delete (blocked if balance non-zero or synced transactions assigned)
- `PUT /api/groups/{groupId}/line-items/reorder` — update sort order
- `GET /api/line-items/{id}/activity` — get transactions + rollover history

### Transactions
- `GET /api/transactions/new` — unassigned transactions
- `GET /api/transactions/tracked?fromDate=...` — assigned transactions from date
- `GET /api/transactions/deleted` — soft-deleted transactions
- `GET /api/transactions/pending` — pending bank transactions
- `POST /api/transactions` — manual entry
- `PUT /api/transactions/{id}` — edit (manual: all fields; synced: notes only)
- `POST /api/transactions/{id}/assign` — assign full amount to a single line item (used by drag-and-drop)
- `POST /api/transactions/{id}/unassign` — remove all assignments
- `PUT /api/transactions/{id}/assignments` — atomically replace all assignments with split amounts (used by edit dialog)
- `DELETE /api/transactions/{id}` — soft-delete
- `DELETE /api/transactions/{id}/permanent` — hard-delete (manual only, must be soft-deleted first)
- `POST /api/transactions/{id}/restore` — restore from deleted

### Sync
- `POST /api/sync` — trigger manual sync
- `GET /api/sync/status` — last sync time, status per account

## Real-Time Updates

- V1: manual refresh only
- Future: SignalR/websockets for real-time sync notifications
