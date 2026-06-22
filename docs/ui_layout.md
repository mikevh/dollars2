# UI Layout

## General

- Desktop-only for v1 (no responsive/mobile design)
- Theme options: light, dark, system
- CSS framework: Tailwind

## Main Layout

- Budget on the left, transaction pane on the right
- Month navigation at the top to browse past/future months

## Budget Pane (Left)

- "Left to budget" indicator at the top (Income Planned - Expenses Planned)
- Income group pinned at top (planned / received / remaining)
- Expense groups below with line items (planned / spent / remaining)
- Groups and line items reorderable via drag-and-drop
- Negative remaining values shown in red text
- Clicking a line item replaces the transaction pane with the line item activity pane

## Transaction Pane (Right) — Tabs

### New Tab
- Unassigned transactions from bank sync and manual entry
- Drag-and-drop onto line items to assign
- Shows all unassigned transactions regardless of date
- Manual entry button
- Sorted by date

### Tracked Tab
- All transactions assigned to a line item
- Initially shows 2 months back, with a button to load 2 more months
- Clicking a transaction opens the edit dialog modal

### Deleted Tab
- Soft-deleted transactions
- Can restore synced transactions (returns to "New" tab)
- Can hard-delete manual transactions

### Pending Tab
- Pending bank transactions (not yet posted)
- View-only

### Search
- Search bar below tabs
- Searches on name and amount (as text)
- Applies to the currently active tab

## Line Item Activity Pane

- Replaces the transaction pane when a line item is clicked
- Shows transactions assigned to this line item for the current month
- Shows a line for the incoming rollover value
- Clicking a transaction opens the edit dialog modal
- Back button/action to return to the transaction pane

## Transaction Edit Dialog (Modal)

- Synced transactions: read-only fields (date, description, amount, account) + editable notes + line item assignment with split amounts
- Manual transactions: all fields editable
- Multiple line item assignments with amount inputs for splits (amounts must total to transaction amount)
- Actions: unassign, delete

## Settings Page

- Manage Plaid and SimpleFIN connections (future version — v1 uses direct DB setup)
- User profile
