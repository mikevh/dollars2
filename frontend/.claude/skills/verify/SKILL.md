---
name: verify
description: Exercise the Dollars2 frontend in a real browser (Playwright) to confirm a UI change works — drive the affected screen, screenshot light + dark, and check for console errors. Use for any change to frontend/src that has a visible/interactive surface.
---

# Verify (frontend)

Runtime observation for the React app: build isn't enough — run the app, drive
the changed screen in a real browser, and capture what renders.

**Delegate this to the `ui-verify` subagent.** That agent runs on a cheaper
model and, critically, reads the screenshots in its own context — so the heavy
image tokens never hit this session. You get back a short PASS/FAIL verdict, not
the raw PNGs.

Spawn it with the Agent tool (`subagent_type: ui-verify`). In the prompt, tell
it:

- **What changed** — the component/screen and what behavior to confirm.
- **Which route(s)** to drive (e.g. `http://localhost:5173/login`), and whether
  the route needs auth (seed a token + run the backend) or is a pure visual check.
- **Any interaction flow** to exercise (typing, clicking, validation) beyond the
  light/dark screenshots.
- Whether to `SendUserFile` the screenshots back (remote surface) or just report
  the verdict.

The subagent starts the dev server, screenshots light + dark via
`npm run ui:shot`, drives any flow with a throwaway Playwright script, reads the
PNGs itself, and returns a verdict naming any token/layout/console-error
problems (plus screenshot paths if you want to look).

Relay the subagent's verdict to the user. If it reports a FAIL, fix it and
re-dispatch. Don't substitute `npm test` / `tsc` for this — run those too, but
the evidence here is the rendered app.
