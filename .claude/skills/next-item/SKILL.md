---
name: next-item
description: Work one small single-concern item end-to-end in an isolated worktree — pick the GitHub issue, make sure it's fully understood (interview if underspecified), plan it, define how it will be verified, implement it, write tests, review, and open a PR. Use when the user says "next item", "work issue #N", "grab the next item", "do the next thing", or points at an issue and says work it.
---

# Next Item

Take one unit of work from issue to open PR in an isolated git worktree, following this project's
conventions. One **small, single-concern** item per invocation (`[[feedback-small-sprints]]` — err
toward too small; split anything that smells like two concerns).

Invoking this skill IS the user's instruction to go all the way through commit, push, and PR — a
scoped override of the standing "never commit/push without instruction" gate
(`[[feedback-commit-push]]`), valid only inside this workflow.

## Steps

### 1. Pick the item
- If the user named an item (usually a GitHub issue number), use it.
- Otherwise list open issues (`gh issue list`) and let the user choose — don't pick for them.

### 2. Understand the item (interview if underspecified)
- Read the issue in full (`gh issue view <N> --comments`) plus the relevant `docs/*.md` specs and
  the source it touches.
- **If you don't fully understand the behavior, scope, and acceptance criteria, interview the user
  before writing any code** — ask targeted questions, verify your understanding back, and don't
  start until the ambiguity is resolved.
- Restate the item as a single concern. If it's really two, propose splitting and do only the first.

### 3. Plan
- Short plan: files to change, the approach, what's out of scope. Smallest independently shippable
  increment.

### 4. Define verification
- State concretely how "done" will be proven — the observable behavior the tests and `/verify` will
  target. If it can't be verified, narrow the scope until it can.

### 5. Record the refined spec on the issue
- Fold the refined understanding into the GitHub issue before coding (`gh issue edit <N> --body ...`):
  interview decisions, single-concern restatement, plan, and acceptance check. The issue body should
  stand on its own as the spec.

### 6. Enter a worktree
- Do ALL work in a dedicated worktree off `master`, never the primary checkout (EnterWorktree, or
  `git worktree add`). Branch: short kebab-case describing the concern.

### 7. Implement
- Follow `CLAUDE.md`: curly braces on all conditionals; multi-mutation API calls in a `DbSession`
  transaction; `DollarsApiResponse<T>` envelope; business-rule violations return error results, not
  exceptions; secrets in user-secrets; new migrations use `ScriptName` and `IF NOT EXISTS` guards.
- Match surrounding style (inline-editing patterns, fixed-height rows, etc.).

### 8. Write tests
- Cover the step-4 acceptance check and the core logic paths.
- Run: backend `dotnet test` and `dotnet build`; frontend `npm test` (non-watch) and `npx tsc --noEmit`.

### 9. Verify and review
- Run the `verify` skill to exercise the change end-to-end and confirm the step-4 acceptance check holds.
- Run `code-review` on the diff and address findings. Capture any deliberately deferred finding as a
  `followup_*` memory + a `MEMORY.md` index line.

### 10. Commit, push, PR, clean up (full auto)
- Commit in the repo's style (imperative, concise), ending with the `Co-Authored-By: Claude Opus 4.8`
  trailer.
- Push and open a PR against `master` (`gh`), linking the issue (`Closes #N`); PR body ends with the
  `🤖 Generated with [Claude Code]` line. Report the PR URL.
- Remove the worktree: confirm the HEAD commit is on the remote (`git branch -r --contains <sha>`
  shows `origin/<branch>`), then `ExitWorktree` `action: "remove"` with `discard_changes: true` (a
  pushed commit can look "unmerged" against a stale local `master`). Don't remove if the push failed
  or changes are uncommitted — leave it and say so.
- Note that the PR branch lives only on the remote; review changes need a fresh checkout.

## Notes
- Stop and ask when: no item named (list issues), the issue is underspecified (interview), scope
  needs splitting, or verification can't be defined — not for routine progress.
- Skip the `verify` skill if the change is docs/tests only with no runtime surface.
