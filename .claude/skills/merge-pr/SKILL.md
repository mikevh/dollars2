---
name: merge-pr
description: Take a pull request from "ready" to fully merged and closed out — re-run all tests against the PR head, rebase-merge it, delete the branch from GitHub, and close associated issues. Use when the user says to merge a PR, e.g. "merge PR #22", "merge and close #22", or "/merge-pr 22".
---

# Merge PR

Take one pull request through the full merge-and-close sequence. Invoking this skill IS the user's
"give the word" — run the steps in order and **abort if any step fails; never merge on red and never
force anything on failure**.

Requires a PR number as the argument (`/merge-pr <N>`). The `next-item` workflow leaves PR branches
remote-only, so "the current branch" is unreliable — always operate on the explicit number.

## Steps

### 1. Confirm the PR
- `gh pr view <N> --json number,title,state,headRefName,mergeable,closingIssuesReferences`.
- Verify it is `OPEN` and `mergeable` is `MERGEABLE`. If it is closed, already merged, has conflicts,
  or is otherwise not mergeable, stop and report — do not attempt to resolve conflicts here.

### 2. Re-run all tests against the PR head
- Check out the PR branch in an isolated worktree so the primary checkout is untouched:
  ```
  git worktree add /tmp/merge-pr-<N> "$(gh pr view <N> --json headRefName -q .headRefName)"
  ```
  (or `cd` into a fresh worktree and `gh pr checkout <N>`).
- Run the full suite from that worktree:
  ```
  cd backend && dotnet build && dotnet test
  cd ../frontend && npx tsc --noEmit && npm test   # npm test == vitest run (non-watch)
  ```
- **If build, typecheck, or any test fails, stop and report the failure. Do not merge.**

### 3. Merge (rebase)
- `gh pr merge <N> --rebase --delete-branch`.
- Rebase replays the PR commits onto `master` with no merge commit. `--delete-branch` removes the
  remote branch (and the local tracking branch if present), covering step 4.
- If the merge fails (e.g. the rebase can't apply cleanly because `master` moved), stop and report —
  do not force-push or hard-reset.

### 4. Confirm the branch is deleted from GitHub
- `--delete-branch` above deletes the remote branch. Verify it is gone:
  `gh api repos/:owner/:repo/branches/<headRefName>` should 404, or the branch is absent from
  `gh pr view <N> --json headRefName` context / `git ls-remote --heads origin <headRefName>` returns
  nothing. If it still exists, delete it explicitly and report.

### 5. Close associated issues
- A rebase merge with `Closes #N` keywords in the PR body auto-closes the referenced issues, but
  confirm explicitly. Read the linked issues:
  `gh pr view <N> --json closingIssuesReferences -q '.closingIssuesReferences[].number'`.
- For each linked issue still `OPEN`, close it: `gh issue close <ISSUE> --comment "Merged in #<N>."`.
- Report which issues were closed.

### 6. Clean up
- Remove the temporary test worktree: `git worktree remove /tmp/merge-pr-<N> --force` (or
  `git worktree remove` the fresh worktree you created).
- Report the outcome: PR merged, branch deleted, and the list of issues closed.

## Notes
- User-invoked only — this skill is never triggered automatically.
- One PR per invocation.
- On any failure, stop and report the exact error rather than retrying blindly; never force-push,
  hard-reset, or discard changes to recover.
