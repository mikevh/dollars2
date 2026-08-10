---
name: verify
description: Hand off a Dollars2 frontend UI change for the user to check in a real browser — confirm the affected screen and behavior, light + dark. Use for any change to frontend/src that has a visible/interactive surface.
---

# Verify (frontend)

Runtime observation for the React app: build isn't enough — the change needs to be seen rendering.
There is no automated browser-verification agent for this; it's a manual handoff to the user.

## What to do

- Say **what changed** — the component/screen and the behavior it should now have.
- Give **clear, concrete instructions**: which route(s) to open (e.g.
  `http://localhost:5173/login`), whether the route needs auth, and any interaction to try
  (typing, clicking, a validation case).
- Name what "correct" looks like — both themes if the change touches anything themed, specific
  states if the change is conditional (error, empty, loading).
- **Stop and wait for the user to confirm** before continuing (committing, opening the PR, moving
  to the next step). Don't screenshot or drive the browser yourself as a substitute.
- If the user reports a problem, fix it and hand off again for the same check.

Don't substitute `npm test` / `tsc` for this — run those too, but the evidence here is the
rendered app, and only the user can supply that now.
