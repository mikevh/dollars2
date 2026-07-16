# Dollars2

A self-hosted, zero-based budgeting web app modeled after
[EveryDollar](https://www.everydollar.com/). Every dollar of income is assigned
to a budget category until remaining equals $0. Multi-user, with separate data
per user, and dual bank-sync providers (Plaid free tier + SimpleFIN).

## Tech Stack

- **Frontend:** React, TypeScript, Vite, Tailwind CSS v4, Redux Toolkit,
  dnd-kit, react-hot-toast
- **Backend:** .NET 10, ASP.NET Core Web API, Dapper, raw SQL
- **Database:** MSSQL, raw SQL migrations (numbered, tracked in a migrations
  table)
- **Auth:** Email-only login (v1), JWT (30-day) + refresh tokens

## Repository Layout

```
frontend/               React app (Vite)
backend/
  Dollars2.Api/         .NET 10 Web API
  Dollars2.Tests/       Backend unit + integration tests
docs/                   Detailed product and technical specs
docker-compose.yml      Self-host deployment (frontend + backend)
```

## Development

### Frontend

```bash
cd frontend
npm install
npm run dev              # Vite dev server
npx tsc --noEmit         # type check
npm test                 # Vitest (single run)
```

### Backend

```bash
cd backend/Dollars2.Api
dotnet run               # start the API
dotnet build             # build

cd ../Dollars2.Tests
dotnet test              # unit + integration tests
```

> **Note:** the integration tests spin up an ephemeral MSSQL instance via
> [Testcontainers](https://testcontainers.com/), so a running Docker daemon is
> required for `dotnet test`.

## Configuration

Secrets are supplied through environment variables and are **never** committed
to `appsettings.json`.

For local development, use
[.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

```bash
cd backend/Dollars2.Api
dotnet user-secrets set "Jwt:Secret" "<random secret>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<MSSQL connection string>"
```

For containerized deployment, copy `.env.example` to `.env` and fill in the
values (JWT secret, SQL connection string, CORS origin, Plaid credentials, and
the API base URL baked into the frontend build), then run:

```bash
docker compose up -d --build
```

## Documentation

Detailed product and technical specs live in [`docs/`](docs/):

- [`project_overview.md`](docs/project_overview.md) — what the app is and why it exists
- [`tech_stack.md`](docs/tech_stack.md) — stack and deployment target
- [`budget_structure.md`](docs/budget_structure.md) — budgets, groups, line items, rollover, the zero-based equation
- [`transaction_handling.md`](docs/transaction_handling.md) — bank sync, manual entry, drag-and-drop assignment, splits
- [`accounts.md`](docs/accounts.md) — per-user accounts and connection details
- [`auth_users.md`](docs/auth_users.md) — email-only login, JWT + refresh tokens, multi-user isolation
- [`ui_layout.md`](docs/ui_layout.md) — budget + transaction pane layout, activity pane, edit dialog
- [`backend.md`](docs/backend.md) — architecture, API endpoints, bank-sync service, provider abstraction
- [`frontend.md`](docs/frontend.md) — UI components, interactions, theme, routing, data fetching
- [`database.md`](docs/database.md) — full schema
- [`out_of_scope.md`](docs/out_of_scope.md) — explicitly excluded/deferred features
