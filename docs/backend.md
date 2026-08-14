# Backend

## Stack

- .NET 10, ASP.NET Core Web API with controllers
- Dapper (ORM)
- MSSQL
- DynamoDB (`amazon/dynamodb-local`, self-hosted — no AWS account) for the sync archive; see
  `docs/sync_archive.md`
- Built-in DI, built-in Microsoft logging

## Project Structure

- `Dollars2.Api` — the single application project
- `Dollars2.Tests` — xUnit unit tests plus a Testcontainers-backed integration suite that runs
  migrations against an ephemeral MSSQL container, so `dotnet test` needs a running Docker daemon.
  `ConstraintNamingTests` fails the build if a system-named constraint reaches the schema

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

- Passkey-only login (no passwords) — WebAuthn ceremony driven directly through ASP.NET Core
  Identity's `IPasskeyHandler<User>` (a Dapper-backed `IUserStore`/`IUserPasskeyStore`, not EF
  Core), never through `SignInManager`'s cookie-based sign-in
- JWT with 30-day expiration
- Refresh tokens
- JWT secret via `dotnet user-secrets` locally / environment variable in the container deploy —
  `appsettings.json` holds only the `<dotnet user secret>` placeholder
- Users created directly in the database; an admin sets `Users.RegistrationKey` (direct SQL
  `UPDATE`) to authorize a passkey registration
- **Retention:** a login or refresh mints a new refresh-token row, and a token that is never used
  again (cleared browser, second device, failed refresh) would otherwise linger forever. Both auth
  paths delete the acting user's already-expired rows as part of the same transaction, so the table
  stays bounded without a scheduled job. No configuration — "expired" is defined by the row's own
  `ExpiresAt`.

## Validation

- Input validation: data annotations on request DTOs
- Business rule validation: return error results from service methods (not exceptions)
- **Money precision:** every client-supplied amount (transaction amount, split assignment amount,
  line item planned amount) is rejected with `INVALID_AMOUNT_PRECISION` when it is finer than a
  cent, rather than being rounded into the `decimal(18,2)` column — the stored value is always the
  one the user entered. `Money.IsWholeCents` is the single gate; the money inputs in the UI refuse
  the keystroke so the error is a backstop, not the normal path

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
- Scripts contain no `GO` batches — both `migrate.ps1` and the test `MigrationRunner` execute each
  file as a single batch, so anything needing multiple steps uses dynamic SQL inside the guard
- Every constraint is named explicitly (`CONSTRAINT <name> ...`), never the inline shorthand — see
  the naming table in `docs/database.md`; `ConstraintNamingTests` enforces it
- Connection string via `dotnet user-secrets` locally / environment variable in the container
  deploy — `appsettings.json` holds only the `<dotnet user secret>` placeholder

## Logging

- Serilog, configured in one place: `Logging/SerilogConfiguration.ConfigureDollars2Logging`
- Sinks:
  - **Console** — always on
  - **Rolling file** — `logs/dollars2-<date>.log`, daily rollover, 14 files retained
  - **Elasticsearch** — added only when `Elasticsearch:Uri` is configured; ships logs to the
    `logs-dollars2` data stream. Absent in local dev / tests / CI, so no Elasticsearch is required
    to run or test the app. An unreachable Elasticsearch never takes the app down — console + file
    logging continue regardless.
- In the home-server deployment, `Elasticsearch` and `Kibana` run as compose services on the same
  host; see `docs/deployment.md` for the full stack topology.

## Bank Sync

- `IHostedService` with a timer running every hour
- Checks each account's configured sync interval to determine if a sync is needed
- Minimum sync interval: 6 hours, configurable per provider via `Plaid:MinSyncIntervalHours` /
  `SimpleFin:MinSyncIntervalHours`
- Manual sync endpoints for on-demand sync, for the whole user or one connection at a time. A
  "connection" is the set of accounts sharing provider credentials, derived from
  `ConnectionDetailsJson` — there is no Connections table
- `SyncLockService` allows one in-flight sync per user; a second request gets 409 `SYNC_IN_PROGRESS`
- Only imports posted transactions; pending transactions shown separately
- Deduplication via provider transaction ID
- Re-synced soft-deleted transactions: set isDeleted back to false
- On sync failure: log and wait for next scheduled run (Polly retries in future versions)
- Sync status exposed to frontend (last sync time, status per account)
- On each successful account sync, the provider-reported current balance is appended to
  `AccountBalances` (in the same per-account transaction as the transaction upserts and sync-log
  entry), building a balance time-series. A null/unparseable balance records no row.
- **`SyncLog` retention:** the hourly loop prunes `SyncLog` after each sync run, deleting rows older
  than `Retention:SyncLogDays` (default 90). There is no SQL Agent in the self-hosted container, so
  the app drives its own retention. Two rows per account are kept regardless of age: the newest row
  (backing the per-account sync status the frontend shows) and the newest **successful** row, which
  is the incremental-sync watermark — pruning it away would silently reset that account to a full
  180-day refetch. The prune runs in its own scope and its own try/catch, so a prune failure never
  stops syncing and a sync failure never skips the prune.
- **Sync archive:** after each account is synced (including on a failed account, whose provider
  errors are exactly what the archive should capture), the raw provider payload for every
  transaction, removal, error, and the account metadata is written to a separate DynamoDB store as
  an append-only, versioned audit trail. This is best-effort — a DynamoDB outage never fails or
  rolls back the sync. See `docs/sync_archive.md` for the schema, versioning model, and the
  Plaid/SimpleFIN fidelity difference.

## Provider Abstraction

- `IBankSyncProvider` interface implemented by both providers
- **Plaid:** Going.Plaid SDK
- **SimpleFIN:** raw HTTP calls

## API Endpoints

### Auth
- `POST /api/auth/passkey/register/options` — email + registration key in, WebAuthn creation
  options out; stashes attestation state in a short-lived Data-Protected cookie
- `POST /api/auth/passkey/register/complete` — signed credential in, credential stored and
  registration key cleared
- `POST /api/auth/passkey/login/options` — email in, WebAuthn request options out; stashes
  assertion state in the same cookie
- `POST /api/auth/passkey/login/complete` — signed assertion in, JWT + refresh token out
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

### Accounts
- `GET /api/accounts` — the user's accounts, grouped by connection, with per-account sync status

### Transactions
- `GET /api/transactions/counts` — per-tab counts (New / Tracked / Deleted / Pending)
- `GET /api/transactions/by-account/{accountId}` — paged account transactions
  (`?page=&size=&sort=&dir=`), backing the per-account transactions page
- `GET /api/transactions/new` — unassigned transactions
- `GET /api/transactions/tracked` — assigned transactions from the last 2 months (UTC clock,
  server-owned window — same one `counts` uses, so the two can never disagree)
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
- `POST /api/sync` — sync every connection for the user
- `POST /api/sync/connection/{connectionId}` — sync one connection
- `POST /api/sync/connection/{connectionId}/resync` — full refetch for one connection, ignoring the
  incremental watermark
- `GET /api/sync/status` — last sync time, status per account

All three sync endpoints return 409 `SYNC_IN_PROGRESS` when that user already has a sync running,
and the per-connection pair returns 404 `CONNECTION_NOT_FOUND` for an unknown connection and 400
`PROVIDER_DISABLED` when the connection's provider is disabled in configuration.

### Health
- `GET /api/health` — unauthenticated liveness check

## Real-Time Updates

- V1: manual refresh only
- Future: SignalR/websockets for real-time sync notifications
