---
name: groom
description: Groom the GitHub backlog so issues are ready to work — refine each into a self-contained spec (single-concern restatement, plan, acceptance criteria), split multi-concern issues, fix labels, flag stale/duplicate/close candidates, and suggest a priority order. A groomed issue clears next-item's "do I fully understand this?" bar. Use when the user says "groom", "groom the backlog", "groom #N", or "/groom [N]".
---

# Groom

Get backlog issues into a **ready-to-work** state. The bar is `next-item` step 2: an issue is
groomed when you could answer *yes* to "I fully understand the behavior, scope, and acceptance
criteria" without interviewing again. A groomed issue's body stands on its own as the spec, and the
issue carries the `groomed` label.

Takes an optional issue number:
- **`/groom <N>`** — groom that one issue (interview the user as needed to reach the ready bar).
- **`/groom`** (no number) — triage all open issues first, then drill in.

This skill edits issue bodies, labels, and creates split issues **directly** via `gh`. The one
exception: **never auto-close** issues — close candidates are only flagged for the user's decision.

## Ready bar (definition of "groomed")

An issue is groomed when its body stands alone as the spec, containing:
- **Single-concern restatement** — one concern, in plain terms. If it's really two, it must be split first.
- **Plan** — files/areas to change, the approach, what's explicitly out of scope.
- **Acceptance check** — the concrete, observable behavior that proves it's done (what tests / `verify` target).
- Any **interview decisions** that resolved ambiguity, folded in so they aren't lost.

Only then apply the `groomed` label.

## Steps

### 0. Setup
- Ensure the `groomed` label exists; create it if missing:
  `gh label create groomed --description "Refined to a self-contained, ready-to-work spec" --color 0e8a16`
  (ignore an "already exists" error).

### 1. Gather
- **Single issue:** `gh issue view <N> --comments`.
- **All issues:** `gh issue list --state open --limit 100` for the set, then read the ones you'll act on.
- For any issue you groom, also read the relevant `docs/*.md` specs and the source it touches — the
  body must be grounded in how the code actually works, not guesses.

### 2. Triage (batch mode)
When no number was given, first classify every open issue into one bucket — do **not** interview yet:
- **Ready** — already meets the ready bar (label `groomed` if not already).
- **Needs info** — underspecified; needs an interview to reach the bar.
- **Split** — smells like two-plus concerns (`[[feedback-small-sprints]]`).
- **Close candidate** — stale, duplicate, or out-of-scope/`wontfix`. Flag only; never close.

Report the buckets as a short summary. Then let the user pick which to drill into — don't groom the
whole list unprompted.

### 3. Refine each issue to the ready bar
For a single issue, or each one the user picks from triage:
- **Interview when underspecified** — ask targeted questions about behavior, scope, and acceptance
  criteria; verify your understanding back before writing anything. Don't guess past real ambiguity.
- **Restate as a single concern.** If it's two, go to step 4 (split) instead of cramming.
- Write the refined body (restatement, plan, acceptance check, interview decisions) and apply it:
  `gh issue edit <N> --body <refined>`. The body must read as a standalone spec.
- Fix labels to match reality (`gh issue edit <N> --add-label ... --remove-label ...`): `bug`,
  `enhancement`, `tech-debt`, `testing`, `ci`, `documentation` as appropriate.
- Apply `groomed` once the body clears the ready bar: `gh issue edit <N> --add-label groomed`.

### 4. Split multi-concern issues
- When an issue holds two-plus concerns, propose the split (titles + one-line scope each), then create
  the new issues directly: `gh issue create --title ... --body ... --label ...`.
- Cross-link: note the split in the original issue's body and each child's body. Narrow the original
  to its first concern (or close it in favor of the children — but per the close rule, **flag that
  for the user**, don't close it yourself).

### 5. Flag close candidates
- List stale / duplicate / out-of-scope issues with a one-line reason and the suggested disposition
  (`close`, `duplicate` of #M, `wontfix`). **Do not close them** — leave the decision to the user.

### 6. Suggest priority order
- End with a recommended order across the groomed/ready issues: what to pick up next and why (blockers,
  quick wins, dependencies). This is advice — it changes no issue state.

### 7. Report
- Summarize what changed: bodies refined, labels applied, issues split/created, `groomed` labels added,
  close candidates flagged (with reasons), and the suggested priority order.

## Notes
- User-invoked only; never triggered automatically.
- **Never auto-close** an issue — always flag close candidates for the user.
- Interviews happen in single-issue mode and when the user drills into a `needs-info` issue from triage;
  batch triage itself classifies without interviewing.
- Grooming produces specs, not code — it never enters a worktree or writes app code. That's `next-item`'s job.
- Err toward too-small single concerns (`[[feedback-small-sprints]]`); when in doubt, split.
