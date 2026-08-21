# Dollars2

Zero-based budgeting web app (EveryDollar clone). Self-hosted, multi-user with separate data per user.

## Tech Stack

- **Frontend:** React, TypeScript, Vite, Tailwind CSS v4, Redux Toolkit, dnd-kit v6, react-hot-toast
- **Backend:** .NET 10, ASP.NET Core Web API, Dapper, raw SQL
- **Database:** MSSQL, raw SQL migrations (numbered, tracking table)
- **Sync archive:** DynamoDB (`amazon/dynamodb-local`, self-hosted — no AWS account)
- **Auth:** Passkey login (WebAuthn), JWT 30-day + refresh tokens

## Project Structure

```
frontend/          React app (Vite)
backend/           .NET 10 Web API (single project: Dollars2.Api)
docs/              Detailed specs (read these for full context)
```

## Docs

Detailed product specs live in `docs/`. Read these before building new features:

- `docs/project_overview.md` — What this app is and why it exists
- `docs/budget_structure.md` — Monthly budgets, groups, line items, rollover mechanics, zero-based equation
- `docs/transaction_handling.md` — Bank sync (Plaid/SimpleFIN), manual entry, drag-and-drop assignment, splits, deletion
- `docs/accounts.md` — Per-user accounts, JSON connection details, v1 direct DB setup
- `docs/auth_users.md` — Passkey login (WebAuthn), JWT + refresh tokens, multi-user isolation
- `docs/ui_layout.md` — Page layout geometry only (pane split, scroll/pin behavior); component
  behavior lives in `docs/frontend.md`
- `docs/backend.md` — Architecture, all API endpoints, bank sync service, provider abstraction
- `docs/frontend.md` — All UI components, interactions, theme, routing, data fetching
- `docs/database.md` — Full schema (all tables, columns, types, constraints, relationships)
- `docs/sync_archive.md` — Append-only DynamoDB record of raw provider payloads: key schema,
  versioning, Plaid/SimpleFIN fidelity, endpoints, frontend views
- `docs/out_of_scope.md` — Explicitly excluded/deferred features

Operational runbooks — not feature context, read only when doing that specific job:

- `docs/deployment.md` — Compose stack topology (six services), config, deploy path, log/backup pointers
- `docs/backups.md` — MSSQL backup job on claw, schedule/retention, and restore procedures

## Conventions

- Always use curly braces on conditional/loop statements, even single-line bodies
- Any API calls with multiple DB mutating calls must be wrapped in a DbSession transaction
- JWT secret and SQL connection string stored in dotnet user-secrets, NOT appsettings.json (placeholders: `<dotnet user secret>`)
- Backend envelope response pattern: `DollarsApiResponse<T>` with `{ data, error }` — both fields always present
- Instants are `DateTime`, stored/returned as UTC and always serialized with a `Z` marker (global
  `UtcDateTimeConverter`); calendar dates are `DateOnly` and serialize bare (`2026-07-22`). Never model a
  calendar date as a `DateTime` — that's what keeps the global converter from shifting dates a day back
- Business rule violations return error results, not exceptions
- Frontend: fetch API into Redux thunks for state the store models (no Axios, no React Query); a
  component may call the `api` client directly for dialog-scoped/ephemeral state that isn't in the
  store. Toast for errors either way
- Inline editing pattern: click to edit, Enter/blur saves, Escape cancels
- `onMouseDown preventDefault` on action buttons adjacent to inputs (prevents blur from hiding buttons before click)
- Fixed height rows (`h-10`) with `border border-transparent px-2 py-0.5` on spans to match input dimensions
- Migration scripts use `ScriptName` column (not `Name`) in the Migrations table
- Migrations 006+ have `IF NOT EXISTS` idempotency guards
- Migration scripts contain no `GO` batches — each file is executed as a single batch
- Name every constraint explicitly — `PK_<Table>`, `FK_<Table>_<RefTable>` (`FK_<Table>_<Column>` when
  a table has 2+ FKs to the same table), `UQ_<Table>_<Cols>`, `DF_<Table>_<Col>`, `CK_<Table>_<Rule>`,
  `IX_`/`UX_<Table>_<Cols>`. Never the inline `PRIMARY KEY`/`REFERENCES`/`UNIQUE`/bare `DEFAULT`
  shorthand: it silently produces a per-database generated name no later migration can reference.
  `ConstraintNamingTests` fails the build if a system-named constraint reaches the schema

## Development

```bash
# Frontend
cd frontend && npm run dev

# Backend
cd backend/Dollars2.Api && dotnet run

# Type check
cd frontend && npx tsc -b --force

# Lint (must stay clean — zero errors, zero warnings)
cd frontend && npm run lint

# Backend build
cd backend/Dollars2.Api && dotnet build
```

### Subagents (keep heavy output off the main session)

Pinned to `model: sonnet` and reports back as condensed text.

- **`test-runner`** (`.claude/agents/test-runner.md`) — runs `dotnet build`,
  `dotnet test`, `npm test`, `npx tsc -b --force`, and `npm run lint` in the current
  worktree and returns pass/fail per check with trimmed failure detail (test name, assertion,
  `file:line`), keeping MSBuild/vitest/Testcontainers output out of context.

### Visual verification (manual handoff)

There is no automated browser-verification agent — visual checks are handed off to the user.
When a change alters what renders (frontend components, styles/tokens, or an API response shape
the UI displays):

- Say what changed and give clear, concrete instructions for what to check: the route/screen to
  open, the interaction to try, and what "correct" looks like (light + dark, specific states).
- **Stop and wait for the user to confirm** before continuing (moving on to commit/PR/next step).
- Don't screenshot or drive the browser yourself as a substitute for this handoff.

## Sprint Approach

- Break work into the smallest possible increments, one concern per sprint
- Interview for specs, verify decisions explicitly
- Never commit or push unless explicitly told to — tell the user things are ready, wait for
  instruction. Invoking `/next-item` is that instruction, scoped to that one item

### The workflow loop

1. **`/new-issue <description>`** — capture work as one-concern GitHub issues
2. **`/groom [N]`** — refine to a self-contained spec; applies the `groomed` label
3. **`/next-item [N]`** — claim, plan, implement, test, open a PR (groomed issues only)
4. **`/review <PR#>`** — user-invoked code review *of the open PR*; `next-item` pauses for it and
   pushes the fixes to the same branch
5. **Merge** — normally by hand in the GitHub UI; `/merge-pr <PR#>` when the full suite should be
   re-run against the PR head first

Review happens on the PR, not on an uncommitted diff. There is no CI, so nothing tests a PR head
unless `/merge-pr` does.

## Backlog

Work items are tracked in **GitHub Issues** (`gh issue list` / https://github.com/mikevh/dollars2/issues).
Review open issues at the start of a work session; open new issues for follow-ups and deferred
code-review findings. Labels: `bug`, `enhancement`, `documentation`, `tech-debt`, `testing`, `ci`,
`wontfix`. Deliberately-deferred v1 decisions are recorded as issues closed with `wontfix`.

## Out of Scope (v1)

Transfers, account management UI, open registration, auto-categorization, debt tracking,
shared budgets, reporting/charts, mobile, CSV import/export, recurring transactions.

`docs/out_of_scope.md` is the authoritative list — update it there, not here.
