---
name: next-item
description: Work one small single-concern item end-to-end in an isolated worktree — pick the GitHub issue, make sure it's fully understood (interview if underspecified), plan it, define how it will be verified, implement it, write tests, review, and open a PR. Use when the user says "next item", "work issue #N", "grab the next item", "do the next thing", or points at an issue and says work it.
---

# Next Item

Take one unit of work from issue to open PR, in an isolated git worktree, following this
project's conventions. One **small, single-concern** item per invocation (see
`[[feedback-small-sprints]]` — err on the side of too small; split anything that smells like two
concerns).

Invoking this skill IS the user's explicit instruction to go all the way through commit, push, and
PR. That is a scoped override of the standing "never commit/push without instruction" gate
(`[[feedback-commit-push]]`) — it applies only inside this workflow. Outside it, the gate still holds.

## Steps

### 1. Pick the item
- If the user named an item (usually a GitHub issue number, e.g. "work #42"), use it.
- Otherwise, list the open GitHub issues (`gh issue list`) and present them for the user to
  choose from. Do NOT pick for them — wait for their choice before proceeding.

### 2. Understand the item (interview if underspecified)
- Read the chosen issue in full (`gh issue view <N> --comments`) plus the relevant `docs/*.md`
  specs and the source it touches.
- Judge whether the issue is well documented — i.e. whether you fully understand the desired
  behavior, scope, and acceptance criteria from what's written.
- **If it is NOT well documented, interview the user before writing any code.** Ask targeted
  questions until you fully understand the intended behavior, edge cases, and what "done" means.
  Verify your understanding back to the user explicitly. Do not begin implementation until the
  ambiguity is resolved.
- Restate the item as a single concern. If it's actually two, propose splitting and do only the
  first.

### 3. Plan
- Write a short plan: files to change, the approach, and what's explicitly out of scope for this
  item.
- Keep it to the smallest increment that is independently shippable.

### 4. Define verification
- Before writing code, state concretely how "done" will be proven — the observable behavior that
  must hold. This is the acceptance check the tests and `/verify` will target, and it is how you
  know the item is complete.
- If it can't be verified, the scope is wrong; narrow it until it can.

### 5. Enter a worktree
- Do ALL work for this item in a dedicated git worktree off `master`, never in the primary
  checkout. Use the EnterWorktree tool (or `git worktree add`).
- Branch name: short kebab-case describing the concern.

### 6. Ensure test infrastructure (first run only)
This repo starts with no test harness. Before writing tests, check and, if missing, set it up:
- **Backend:** create `backend/Dollars2.Tests` (xUnit), reference `Dollars2.Api`, add a `.sln` that
  includes both projects so `dotnet test` works. Match .NET 10 / the API's target framework.
- **Frontend:** add Vitest + `@testing-library/react` + `jsdom`, a `test` script in
  `package.json`, and a `vitest.config.ts`.
- Setting up the harness may be large enough to be its own item — if so, stop after this step,
  report the harness is ready, and let the user re-invoke for the feature itself.

### 7. Implement
- Follow the conventions in `CLAUDE.md`: curly braces on all conditionals; multi-mutation API calls
  wrapped in a `DbSession` transaction; `DollarsApiResponse<T>` envelope; business-rule violations
  return error results, not exceptions; secrets in user-secrets. New migrations use the
  `ScriptName` tracking column and `IF NOT EXISTS` guards.
- Match surrounding style (inline-editing patterns, fixed-height rows, etc.).

### 8. Write tests
- Cover the acceptance check from step 4 and the core logic paths of the change.
- Run them: `cd backend && dotnet test`, and `cd frontend && npm test` (non-watch). Also
  `npx tsc --noEmit` for the frontend and `dotnet build` for the backend.

### 9. Verify and review
- Run the `verify` skill to exercise the change end-to-end (drive the real flow, not just tests)
  and confirm the acceptance check from step 4 holds — this is how you confirm the item is done.
- Run the `code-review` skill on the diff and address findings. If any finding is deliberately
  deferred, capture it as a `followup_*` memory + a `MEMORY.md` index line before moving on.

### 10. Commit, push, PR, clean up (full auto)
- Commit with a message in this repo's style (imperative, concise), ending with the
  `Co-Authored-By: Claude Opus 4.8` trailer.
- Push the branch and open a PR against `master` with `gh`, linking the issue (e.g. `Closes #N`).
  PR body ends with the `🤖 Generated with [Claude Code]` line.
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
- Stop and ask the user when: no item was named (present the issue list), the chosen issue is
  underspecified (interview per step 2), the scope needs splitting, or verification can't be
  defined — not for routine progress.
- If the change touches only docs/tests with no runtime surface, skip the `verify` skill.
