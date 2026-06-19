---
name: frontend
description: React/TypeScript frontend spec — Vite, Redux, Tailwind, dnd-kit, all UI details
metadata:
  type: project
---

## Stack

- React with TypeScript
- Vite build tool
- Tailwind CSS (no component library — built from scratch)
- Redux for state management
- React Router for routing
- React Hook Form for forms
- dnd-kit for drag-and-drop
- Font Awesome for icons
- react-hot-toast for toast notifications
- System fonts
- fetch API (no Axios)

## Routing

- `/login` — login page
- `/` — budget view (main app)

## Theme

- Light, dark, system options
- Tailwind `dark:` class strategy
- Toggle in persistent footer
- Preference saved to localStorage

## Login Page

- Email input and submit button
- No password for v1

## App Layout

- No header/navbar
- Budget pane (left) + transaction pane (right)
- Persistent footer: theme toggle, logout button, sync status with manual sync icon button

## Footer

- Sync status: "Last synced: X ago" (aggregate across all accounts)
- Manual sync: icon button next to status
- Theme toggle: light/dark/system
- Logout button

## Budget Pane (Left)

### Month Navigation
- At the top of the budget pane
- Left/right arrows with month/year label (e.g., < June 2026 >)

### Zero-Based Indicator
- Below month navigation
- Shows: Total Income Planned - Total Expenses Planned = Left to Budget
- Green at $0, red/yellow otherwise

### Income Group
- Pinned at top, not deletable or reorderable
- Shows planned / received / remaining per line item

### Expense Groups
- User-created, drag-and-drop reorderable (dnd-kit)
- Not collapsible for v1
- "+ Add Group" button at the bottom of the budget pane

### Line Items
- Drag-and-drop reorderable within their group (dnd-kit)
- Show: planned / spent / remaining
- Planned amount: inline editable (click to edit, spreadsheet-style)
- Remaining: red text if negative
- "+ Add Item" button at the bottom of each group
- Clicking a line item opens the activity pane (replaces transaction pane)

### Currency Input
- USD only
- Allow dollars and cents only
- No formatted input (no auto-commas or dollar sign in input)

## Transaction Pane (Right)

### Tabs
- New, Tracked, Deleted, Pending
- Each tab shows a count badge (e.g., "New (5)")

### New Tab
- Unassigned transactions, sorted by date
- "+" button at top for manual transaction entry
- Drag-and-drop transactions onto line items to assign
- Line items highlight as valid drop targets when dragging
- Default browser drag image for v1

### Tracked Tab
- Assigned transactions, sorted by date
- Initially shows 2 months back
- Button/link to load 2 more months
- Clicking a transaction opens edit dialog (modal)

### Deleted Tab
- Soft-deleted transactions
- Restore action (synced transactions) — returns to New tab
- Hard-delete action (manual transactions only)

### Pending Tab
- Pending bank transactions (not yet posted)
- View-only

### Search
- Search bar below tabs
- Searches on name and amount (as text)
- Applies to the currently active tab

### Transaction Item Display
- Description on the left, amount on the right
- Date and account label as smaller text below
- Income (positive): green text with "+" prefix
- Expense: standard display with "-" prefix
- Manual transactions: account label appended with "-manual"

## Line Item Activity Pane

- Replaces the transaction pane when a line item is clicked
- Header: line item name, planned / spent / remaining
- Transactions assigned to this line item for the current month
- Rollover entry styled like a transaction: "Rollover from May"
- Trash icon for deleting the line item (blocked if balance non-zero or synced transactions assigned)
- Close: X button in top right, or click elsewhere in the budget pane
- Clicking a transaction opens the edit dialog (modal)
- Planned amount edited inline on the budget pane, not from this pane
- No rename from this pane

## Transaction Edit Dialog (Modal)

### Synced Transactions
- Read-only: date, description, amount, account
- Editable: notes, line item assignment(s)
- Split UI: when multiple line items assigned, amount input next to each (must total exactly)
- Actions: unassign, delete (must unassign first)

### Manual Transactions
- All fields editable: date, description, amount, account, notes
- Line item assignment with split support
- Actions: unassign, delete (must unassign first)

## Cross-Month Warning

- When dragging a transaction dated in one month onto a line item in a different month
- OK/Cancel confirmation dialog

## Empty States

- Simple text messages: "No transactions", etc.

## Error Handling

- Toast notifications (react-hot-toast) for API errors

## Loading States

- Simple loading text

## Data Fetching

- fetch API into Redux
- Budget data requeried on each month navigation
