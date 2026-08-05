---
name: new-issue
description: Turn a rough idea, bug report, or brain-dump into well-formed GitHub issues — one concern each, grounded in the actual code and docs/ specs, correctly labeled, checked against the existing backlog for duplicates. The entry point to the workflow; hands off to /groom for the ready-to-work pass. Use when the user says "new issue", "file an issue", "open issues for ...", "/new-issue", or describes work that should be captured rather than done now.
---

# New Issue

Capture work as GitHub issues. This is step 1 of the loop: **new-issue → groom → next-item →
/review → merge**. The job here is *capture*, not full specification — `groom` is what takes an
issue to the ready-to-work bar and applies the `groomed` label.

Takes free text: `/new-issue <description>`, or a brain-dump of several things at once.

This skill creates issues **directly** via `gh`. It never writes app code and never enters a
worktree.

## What "well-formed" means here

Enough that the issue is still legible in three weeks — not a full spec:

- **One concern per issue.** Err toward too small (`[[feedback-small-sprints]]`); when a dump holds
  three things, that's three issues.
- **A title that names the concern**, not the symptom-of-the-day.
- **Body**: what's wrong or wanted, why it matters, and any concrete pointers you found — file paths,
  the relevant `docs/*.md` section, a reproduction. Note open questions explicitly rather than
  guessing an answer into the body.
- **Correct labels** from the project set: `bug`, `enhancement`, `documentation`, `tech-debt`,
  `testing`, `ci`, `wontfix`.

Do **not** apply `groomed` — only `groom` does that, after the plan and acceptance criteria exist.

## Steps

### 1. Split the input into concerns
Read the user's text and separate it into single concerns. Say what you're going to file — titles
plus a one-line scope each — before creating anything. If the split is ambiguous, ask.

### 2. Ground each one in the codebase
Don't file a guess. For each concern:
- **Spawn the `Explore` agent with `model: sonnet`** to find the files, patterns, and `docs/*.md`
  language it touches — this is search-and-summarize work and shouldn't run at Opus in this session.
- Fold the concrete pointers it returns into the body. An issue that names the file is worth far
  more later than one that describes a feeling.
- If exploration shows the thing is already fixed, already the intended behavior, or explicitly
  listed in `docs/out_of_scope.md`, **say so and don't file it** — check with the user first.

### 3. Check the existing backlog
`gh issue list --state open --limit 100` (and `gh issue list --state closed --search "<keywords>"`
for anything that looks familiar). If a concern duplicates or substantially overlaps an open issue,
don't file a second one — report the match and offer to comment on the existing issue instead.

### 4. Create the issues
```bash
gh issue create --title "<title>" --body "<body>" --label "<label>"
```
Cross-link related issues in their bodies where the relationship matters (blocks, follows-on-from,
split-from). Report every issue number and URL created.

### 5. Hand off
End by pointing at the next step: `/groom <N>` to take an issue to the ready bar, or `/groom` to
triage the whole backlog. Don't groom them yourself here — capture and refinement are separate
passes on purpose.

## Notes
- User-invoked only.
- Never closes or edits an unrelated existing issue; commenting on a duplicate requires the user's OK.
- Interview when the concern is genuinely unclear — but keep it light. Deep specification is
  `groom`'s job, and asking twice about the same thing is a waste of the user's time.
- Deliberately-rejected ideas get filed and closed as `wontfix` (per `CLAUDE.md`) so the decision is
  recorded — but **flag that for the user**, don't file-and-close on your own.
