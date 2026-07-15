---
name: next-sprint
description: Run one small single-concern sprint end-to-end in an isolated worktree — pick the next backlog item, plan it, define how it will be verified, implement it, write tests, review, and open a PR. Use when the user says "next sprint", "grab the next item", "do the next thing", or points at a backlog/follow-up item and says work it.
---

# Next Sprint

Take the next unit of work from idea to open PR, in an isolated git worktree, following this
project's conventions. One **small, single-concern** sprint per invocation (see
`[[feedback-small-sprints]]` — err on the side of too small; split anything that smells like two
concerns).

Invoking this skill IS the user's explicit instruction to go all the way through commit, push, and
PR. That is a scoped override of the standing "never commit/push without instruction" gate
(`[[feedback-commit-push]]`) — it applies only inside this workflow. Outside it, the gate still holds.

## Steps

### 1. Pick the item
- If the user named an item, use it.
- Otherwise propose the next item from, in priority order: open **GitHub Issues**
  (`gh issue list`), deferred code-review findings (`followup_*` memories), then open TODOs
  in the code. Show your pick and one alternative; confirm before proceeding.
- Restate the item as a single concern. If it's actually two, propose splitting and do only the
  first.

### 2. Plan
- Read the relevant `docs/*.md` specs and the touched source before planning.
- Write a short plan: files to change, the approach, and what's explicitly out of scope for this
  sprint.
- Keep it to the smallest increment that is independently shippable.

### 3. Define verification
- Before writing code, state concretely how "done" will be proven — the observable behavior that
  must hold. This is the acceptance check the tests and `/verify` will target.
- If it can't be verified, the scope is wrong; narrow it until it can.

### 4. Enter a worktree
- Do ALL work for this sprint in a dedicated git worktree off `master`, never in the primary
  checkout. Use the EnterWorktree tool (or `git worktree add`).
- Branch name: short kebab-case describing the concern.

### 5. Ensure test infrastructure (first run only)
This repo starts with no test harness. Before writing tests, check and, if missing, set it up:
- **Backend:** create `backend/Dollars2.Tests` (xUnit), reference `Dollars2.Api`, add a `.sln` that
  includes both projects so `dotnet test` works. Match .NET 10 / the API's target framework.
- **Frontend:** add Vitest + `@testing-library/react` + `jsdom`, a `test` script in
  `package.json`, and a `vitest.config.ts`.
- Setting up the harness may be large enough to be its own sprint — if so, stop after this step,
  report the harness is ready, and let the user re-invoke for the feature itself.

### 6. Implement
- Follow the conventions in `CLAUDE.md`: curly braces on all conditionals; multi-mutation API calls
  wrapped in a `DbSession` transaction; `DollarsApiResponse<T>` envelope; business-rule violations
  return error results, not exceptions; secrets in user-secrets. New migrations use the
  `ScriptName` tracking column and `IF NOT EXISTS` guards.
- Match surrounding style (inline-editing patterns, fixed-height rows, etc.).

### 7. Write tests
- Cover the acceptance check from step 3 and the core logic paths of the change.
- Run them: `cd backend && dotnet test`, and `cd frontend && npm test` (non-watch). Also
  `npx tsc --noEmit` for the frontend and `dotnet build` for the backend.

### 8. Verify and review
- Run the `verify` skill to exercise the change end-to-end (drive the real flow, not just tests).
- Run the `code-review` skill on the diff and address findings. If any finding is deliberately
  deferred, capture it as a `followup_*` memory + a `MEMORY.md` index line before moving on.

### 9. Commit, push, PR, clean up (full auto)
- Commit with a message in this repo's style (imperative, concise), ending with the
  `Co-Authored-By: Claude Opus 4.8` trailer.
- Push the branch and open a PR against `master` with `gh`. PR body ends with the
  `🤖 Generated with [Claude Code]` line.
- Report the PR URL.
- Then remove the worktree: the branch and all commits are safely on the remote once pushed, so the
  local worktree and branch are no longer needed. Verify the branch's HEAD commit exists on the
  remote (`git branch -r --contains <sha>` shows `origin/<branch>`), then `ExitWorktree` with
  `action: "remove"` (pass `discard_changes: true` — local `master` may be stale, so the merged/pushed
  commit can look "unmerged" locally even though it is safe on the remote). Do NOT remove if the push
  failed or there are uncommitted changes — leave the worktree and say so.
- Note in your report that the PR branch lives on the remote; any review-requested changes need a
  fresh checkout of that branch (the local worktree is gone).

## Notes
- Stop and ask the user only when the pick is ambiguous, the scope needs splitting, or verification
  can't be defined — not for routine progress.
- If the change touches only docs/tests with no runtime surface, skip the `verify` skill.
