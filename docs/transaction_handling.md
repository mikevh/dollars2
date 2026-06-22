# Transaction Handling

## Bank Sync

- Two providers: Plaid (free tier) and SimpleFIN ($1.50/mo)
- Sync frequency: configurable per data source, default every 12 hours
- Manual sync button to trigger on-demand
- Only posted transactions are imported (pending shown in a separate tab)
- Duplicate detection via provider's transaction ID
- If a soft-deleted synced transaction is re-synced, the isDeleted flag is set back to false and it reappears in "New" tab

## Manual Entry

- Created via button in the transaction inbox pane
- Required fields: date, description, amount, account, notes
- All fields are editable after creation
- Manual transactions are visually distinguished by appending "-manual" to the account label

## Transaction Fields

- Date (from bank or user-entered)
- Description (from bank or user-entered)
- Amount (positive for income, negative for expense)
- Account label (with "-manual" suffix for manual transactions)
- Notes (user-editable on all transactions)
- Source account shown as a small label

## Display

- Income transactions (positive amounts): green text with "+" prefix
- Expense transactions: standard display with "-" prefix
- Sorted by date in all views

## Assignment

- Transactions appear in "New" tab (unassigned)
- Drag-and-drop from inbox onto a line item to assign
- A transaction belongs to whichever month's line item it is dropped on (not determined by transaction date)
- Warning dialog (OK/Cancel) if dropping a transaction dated in a different month than the target line item
- Unassigning moves the transaction back to the "New" tab

## Splitting

- Initiated from the transaction edit dialog
- When multiple line items are assigned, an amount input appears next to each
- Split amounts must add up to the transaction total exactly
- No partial splits — entire amount must be allocated
- Splits can be undone (unsplit returns full transaction to inbox)

## Edit Dialog (Modal)

- **Synced transactions:** date, description, amount, account are read-only. Editable: notes, line item assignment(s), split amounts
- **Manual transactions:** all fields editable (date, description, amount, account, notes, line item assignment(s))
- Actions available: unassign, delete

## Deletion

- Transactions must be unassigned before they can be deleted
- Synced transactions: soft-delete only (moved to "Deleted" tab, can be restored to "New" tab)
- Manual transactions: can be hard-deleted from the "Deleted" tab
- Restoring a deleted transaction returns it to "New" tab (unassigned)
