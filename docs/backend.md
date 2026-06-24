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
- Run manually against the DB
- Migrations tracking table to record applied scripts
- Connection string in appsettings.json

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
