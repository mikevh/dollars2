---
name: verify
description: Exercise the Dollars2 frontend in a real browser (Playwright) to confirm a UI change works — drive the affected screen, screenshot light + dark, and check for console errors. Use for any change to frontend/src that has a visible/interactive surface.
---

# Verify (frontend)

Runtime observation for the React app: build isn't enough — run the app, drive
the changed screen in a real browser, and capture what renders. Uses Playwright
with a bundled Chromium (already installed via `npx playwright install
chromium`). No Chrome extension or claude.ai login required.

## 1. Start the dev server

```bash
cd frontend
npm run dev            # serves http://localhost:5173 (note the actual port it prints)
```

Run it in the background (or a separate pane) and note the port. Kill it when
done (`Stop-Process` the `vite` node process, or Ctrl-C the pane).

## 2. Screenshot the changed screen in both themes

`scripts/ui-shot.mjs` launches headless Chromium, seeds the app's own `theme`
localStorage key (so `useTheme` applies `.dark` exactly as in production —
**not** Chrome's auto-invert), reloads, and screenshots. It exits non-zero if
the page logs a console error.

```bash
cd frontend
npm run ui:shot -- --url http://localhost:5173/login --out .ui-shots/login
# → .ui-shots/login-light.png, .ui-shots/login-dark.png
```

Then Read the PNGs to confirm the design (tokens, layout, dark mode). Output
lands in `.ui-shots/` (gitignored). On a remote surface, send them with
`SendUserFile`.

Flags: `--themes light,dark,system`, `--full` (full-page), `--wait <ms>`
(settle time), `--seed '<json>'` (extra localStorage, e.g. an auth token for
authenticated routes). See the header of `scripts/ui-shot.mjs`.

## 3. Drive interactions / assert behavior

For flows (typing, clicking, validation) write a short throwaway Playwright
script — reuse the same `chromium.launch()` + localStorage-seed pattern. Drive
through the real UI (click the button, don't call the thunk), then read the DOM
for the observable result. Example checks that have mattered here:

- Email validation: `type="email"` native constraint validation blocks submit
  for values like `not-an-email` (RHF never runs) — use `a@b` to reach the
  app's own "Invalid email address" message; empty submit → "Email is required".
- Theme: assert `document.documentElement.classList.contains('dark')` and read
  `getComputedStyle(document.body).backgroundColor` (light `rgb(243,242,242)`,
  dark `rgb(24,16,14)`).

## 4. Authenticated routes

`/` (BudgetPage) requires auth. Seed a token and run the backend
(`cd backend/Dollars2.Api && dotnet run`) if the flow needs live data:
`--seed '{"token":"<jwt>"}'`. For pure visual/token checks the login screen is
usually enough.

## Notes

- Don't substitute `npm test` / `tsc` for this — those are CI, not verification.
  Run them too, but the evidence here is the rendered app.
- The Modernist design tokens live in `src/index.css` (`@theme` light values +
  `.dark` overrides). Computed colors are the ground truth for token bugs.
