# UI Layout

## General

- Desktop-only for v1 (no responsive/mobile design)
- Theme options: light, dark, system
- CSS framework: Tailwind

## Main Layout

- Budget on the left, transaction pane on the right
- Month navigation at the top to browse past/future months
- The page has a single (window) scrollbar: the month navigation and the transaction pane stay
  pinned in the viewport while the budget groups scroll underneath them, so the drag-and-drop
  source stays reachable no matter how long the budget list is

## Budget Pane (Left)

See `frontend.md` § Budget Pane (Left) for component behavior (month nav, zero-based indicator,
income group, expense groups, line items, currency input).

## Transaction Pane (Right)

See `frontend.md` § Transaction Pane (Right) for tabs (New/Tracked/Deleted/Pending), search, and
display rules.

## Line Item Activity Pane

See `frontend.md` § Line Item Activity Pane.

## Transaction Edit Dialog (Modal)

See `frontend.md` § Transaction Edit Dialog (Modal).

## Accounts Pages

See `frontend.md` § Routing for the `/accounts` and `/accounts/:accountId` routes.

A settings page (connection management, user profile) is deferred — see `out_of_scope.md`.
