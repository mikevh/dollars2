# Dollars2

Zero-based budgeting web app (EveryDollar clone). Self-hosted, multi-user with separate data per user.

## Tech Stack

- **Frontend:** React, TypeScript, Vite, Tailwind CSS v4, Redux Toolkit, dnd-kit v6, react-hot-toast
- **Backend:** .NET 10, ASP.NET Core Web API, Dapper, raw SQL
- **Database:** MSSQL, raw SQL migrations (numbered, tracking table)
- **Auth:** Email-only login (v1), JWT 30-day + refresh tokens

## Project Structure

```
frontend/          React app (Vite)
backend/           .NET 10 Web API (single project: Dollars2.Api)
docs/              Detailed specs (read these for full context)
```

## Docs

Detailed product specs live in `docs/`. Read these before building new features:

- `docs/project_overview.md` — What this app is and why it exists
- `docs/tech_stack.md` — Stack and deployment target
- `docs/budget_structure.md` — Monthly budgets, groups, line items, rollover mechanics, zero-based equation
- `docs/transaction_handling.md` — Bank sync (Plaid/SimpleFIN), manual entry, drag-and-drop assignment, splits, deletion
- `docs/accounts.md` — Per-user accounts, JSON connection details, v1 direct DB setup
- `docs/auth_users.md` — Email-only login (v1), JWT + refresh tokens, multi-user isolation
- `docs/ui_layout.md` — Budget pane + transaction pane layout, activity pane, edit dialog, tabs
- `docs/backend.md` — Architecture, all API endpoints, bank sync service, provider abstraction
- `docs/frontend.md` — All UI components, interactions, theme, routing, data fetching
- `docs/database.md` — Full schema (all tables, columns, types, constraints, relationships)
- `docs/out_of_scope.md` — Explicitly excluded/deferred features

## Conventions

- Always use curly braces on conditional/loop statements, even single-line bodies
- Any API calls with multiple DB mutating calls must be wrapped in a DbSession transaction
- JWT secret and SQL connection string stored in dotnet user-secrets, NOT appsettings.json (placeholders: `<dotnet user secret>`)
- Backend envelope response pattern: `DollarsApiResponse<T>` with `{ data, error }` — both fields always present
- Business rule violations return error results, not exceptions
- Frontend: fetch API into Redux thunks (no Axios, no React Query), toast for errors
- Inline editing pattern: click to edit, Enter/blur saves, Escape cancels
- `onMouseDown preventDefault` on action buttons adjacent to inputs (prevents blur from hiding buttons before click)
- Fixed height rows (`h-10`) with `border border-transparent px-2 py-0.5` on spans to match input dimensions
- Migration scripts use `ScriptName` column (not `Name`) in the Migrations table
- Migrations 006+ have `IF NOT EXISTS` idempotency guards

## Development

```bash
# Frontend
cd frontend && npm run dev

# Backend
cd backend/Dollars2.Api && dotnet run

# Type check
cd frontend && npx tsc --noEmit

# Backend build
cd backend/Dollars2.Api && dotnet build
```

## Sprint Approach

- Break work into the smallest possible increments, one concern per sprint
- Interview for specs, verify decisions explicitly
- Code review uncommitted changes before committing

## Code Review Backlog (2026-06-19)

Critical items (1-5) fixed in e1d84ba. Medium items (6-12) fixed in subsequent commits. Remaining:

### Low
- Early migrations (000-005) lack `IF NOT EXISTS` guards
- Several repos use `SELECT *` instead of explicit columns
- `new Date()` in frontend components stale if left open overnight
- N+1 queries in `BuildBudgetResponseAsync` (has TODO)
- No rate limiting on auth endpoints (v1 tradeoff)

## Out of Scope (v1)

Transfers, account management UI, passkeys, open registration, auto-categorization, debt tracking, shared budgets, reporting/charts, mobile, CSV import/export, recurring transactions
