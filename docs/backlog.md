# Backlog

Canonical, source-controlled working task list for Dollars2. This is the shared source of truth
across machines — read it at the start of a work session and update it as items move. Keep entries
short; link to `docs/*.md` specs or code paths for detail.

Conventions: newest-relevant first within each section. When an item ships, move it to **Done** with
its PR number. Small, single-concern sprints (see `CLAUDE.md` / the `next-sprint` skill).

## Now (next up)

- **Root `README.md`** — add a repo-root README so the project presents well on GitHub: name +
  one-line description, tech stack, repo layout (`frontend/`, `backend/Dollars2.Api`,
  `backend/Dollars2.Tests`, `docs/`), dev commands (`npm run dev`; `dotnet run`; `dotnet test` — note
  the integration tests need a running Docker daemon for Testcontainers MSSQL), configuration
  (secrets via `dotnet user-secrets`: JWT secret + SQL connection string, never in appsettings.json),
  and a pointer to `docs/`.

## Next

- **Integration-test rollout** — convert a second repository (e.g. `BudgetRepository` or
  `TransactionAssignmentRepository`) to an end-to-end Testcontainers integration test, building on the
  harness from PR #5.
- **CI for the Docker-backed integration tests** — wire `dotnet test` (with the Testcontainers MSSQL
  dependency) into CI.

## Someday / low priority

Low-severity items from the 2026-06-19 full code review (all critical/medium already fixed):

- Early migrations `000`–`005` lack `IF NOT EXISTS` guards (006+ have them).
- Several repositories use `SELECT *` instead of explicit column lists.
- Frontend `new Date()` in components can go stale if a tab is left open overnight.
- N+1 queries in `BuildBudgetResponseAsync` (has a `// TODO`; low impact at current scale).

- **Dark-mode UI redesign** — apply the design handoff when ready (see the `design-handoff-dark-mode`
  memory for how to apply the handoff zip).

## Won't do (v1)

- Rate limiting on auth endpoints — deliberate v1 tradeoff.
- See `docs/out_of_scope.md` for the full deferred-feature list (transfers, account UI, passkeys,
  auto-categorization, mobile, analytics, etc.).

## In review

_(nothing in review)_

## Done

- **PR #7** — Plaid cursor-divergence resync storm fixed (`ResolveGroupCursor`).
- **PR #6** — Plaid removed transactions applied regardless of `account_id`; also fixed manual-entry
  `Payee`/`Memo` plumbing.
- **PR #5** — Testcontainers ephemeral-MSSQL integration test infrastructure + first repository proof.
- **PR #4** — backend rolling-file logging via Serilog.
- **PR #3** — backend xUnit test harness (`Dollars2.Tests` + `Dollars2.slnx`).
- **PR #2** — per-connection bank sync refactor + misconfigured-account handling.
