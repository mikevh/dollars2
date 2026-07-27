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

Delegate the token-heavy phases to the cheaper subagents noted below (`Explore`, `test-runner`,
`ui-verify`) and keep this session on understanding, implementing, and judging.

## Steps

### 1. Pick the item
Only work issues carrying the `groomed` label — the `groom` skill applies it, and it guarantees the
issue body stands alone as a spec.

Multiple agents run in parallel, so an item must be **claimed** before any work starts. An issue is
already taken if it carries `in-progress`, has an assignee, or has an open PR whose body says
`Closes #N`. The label can get dropped by a crashed run; the open PR can't — check both.

- **Named** ("work #N"): `gh issue view <N> --json labels,assignees`. Not groomed is a **hard
  block** — don't start, point the user at `/groom <N>`, stop. No proceed-anyway override. Already
  claimed is also a stop — say who/what holds it and ask, don't take it over.
- **Unnamed**: list the unclaimed groomed items and let the user choose. Don't pick for them. If the
  listing is empty, say so and suggest `/groom`.

  ```bash
  gh issue list --state open --label groomed --search "-label:in-progress no:assignee"
  gh pr list --state open --json number,body \
    --jq '.[] | "PR #\(.number) holds #\(.body | capture("(?i)closes #(?<n>\\d+)").n)"'
  ```

  Drop anything the second command names, even if it survived the first.

**Claim it immediately** once the item is settled, before step 2's exploration:

```bash
gh issue edit <N> --add-label in-progress --add-assignee @me
```

If an issue is labeled `in-progress` but has no worktree in `git worktree list` and no open PR, the
claim is stale — report it and ask before reclaiming. Don't silently steal it.

### 2. Understand the item (interview if underspecified)
- Read the issue in full: `gh issue view <N> --comments`.
- For the surrounding context — which files the change touches, the relevant `docs/*.md` spec
  language, and the existing patterns to match — **spawn the `Explore` agent** and work from its
  brief. Then read only the files you'll actually edit. Don't graze the codebase from this session.
- **If you don't fully understand the behavior, scope, and acceptance criteria, interview the user
  before writing any code** — targeted questions, verify your understanding back, don't start until
  the ambiguity is resolved.

### 3. Refine the spec and record it on the issue
- Restate the item as a **single concern**. If it's really two, propose splitting and do only the first.
- Short plan: files to change, the approach, what's out of scope. Smallest independently shippable
  increment.
- State concretely how "done" will be proven — the observable behavior tests and browser
  verification will target. If it can't be verified, narrow the scope until it can.
- Fold all of that into the issue before coding (`gh issue edit <N> --body ...`): interview
  decisions, single-concern restatement, plan, acceptance check. The body must stand on its own.

### 4. Enter a worktree
- Do ALL work in a dedicated worktree off `master`, never the primary checkout (EnterWorktree, or
  `git worktree add`).
- Branch: `issue-<N>-<short-kebab-slug>` — the issue number makes `git worktree list` a local
  registry of what's in flight, so a parallel agent can see the claim without hitting the API.

### 5. Implement
- Follow `CLAUDE.md`: curly braces on all conditionals; multi-mutation API calls in a `DbSession`
  transaction; `DollarsApiResponse<T>` envelope; business-rule violations return error results, not
  exceptions; `DateOnly` for calendar dates and `DateTime` for instants; secrets in user-secrets; new
  migrations use `ScriptName` and `IF NOT EXISTS` guards.
- Match surrounding style (inline-editing patterns, fixed-height rows, etc.).

### 6. Write and run tests
- Cover the step-3 acceptance check and the core logic paths.
- **Spawn the `test-runner` agent** (`.claude/agents/test-runner.md`, sonnet) to run `dotnet build`,
  `dotnet test`, `npm test`, and `npx tsc --noEmit` and return a compact verdict — build and test
  output doesn't belong in this session. Fix what it reports, then have it re-run.

### 7. Verify and review
- **Spawn the `ui-verify` agent** (`.claude/agents/ui-verify.md`, sonnet) *only when the change
  alters what renders* — frontend components, styles/tokens, or an API response shape the UI
  displays. Point it at the specific changed screen (`--url`), not a general sweep; it drives light
  + dark, confirms the step-3 acceptance check, reads the screenshots itself, and returns a text
  verdict.
- **Skip it** for work with no visible change — migrations, SQL, sync internals, backend-only
  refactors, tests, docs. `npm test` and `tsc` already ran in step 6; booting the dev server and
  Chromium to look at an unchanged screen proves nothing. Say in the PR that you skipped it and why.
- Run the `/code-review` command on the diff and address findings. Capture any deliberately deferred
  finding as a GitHub issue (per `CLAUDE.md`'s backlog convention).

### 8. Commit, push, PR, clean up (full auto)
- Commit in the repo's style (imperative, concise) with the harness's `Co-Authored-By` and
  `Claude-Session` trailers.
- Push and open a PR against `master` (`gh`), linking the issue (`Closes #N`); PR body ends with the
  `🤖 Generated with [Claude Code]` line. Report the PR URL.
- Leave the issue open — `Closes #N` closes it when the PR merges. Don't close it at PR-open time.
- Leave `in-progress` and the assignee on it too: the item stays claimed until the PR merges, and
  closing the issue takes both with it. **Release the claim** (`gh issue edit <N> --remove-label
  in-progress --remove-assignee @me`) only when abandoning the item, or when a split means the
  remainder goes back on the board unworked.
- Remove the worktree: confirm the HEAD commit is on the remote (`git branch -r --contains <sha>`
  shows `origin/<branch>`), then `ExitWorktree` `action: "remove"` with `discard_changes: true` (a
  pushed commit can look "unmerged" against a stale local `master`). Don't remove if the push failed
  or changes are uncommitted — leave it and say so.
- Note that the PR branch lives only on the remote; review changes need a fresh checkout.

## Notes
- Stop and ask when: no item named, a named issue lacks `groomed` (hard block → `/groom <N>`), the
  issue is already claimed or holds a stale claim, the issue is underspecified (interview), scope
  needs splitting, or verification can't be defined — not for routine progress.
- Other agents are working other issues concurrently. Touch only the claimed issue and your own
  worktree; never `git worktree remove` or push a branch that isn't yours.
