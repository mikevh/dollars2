# Budget Structure

## Monthly Budget

- One budget per month per user
- Budget is created by copying the prior month's budget (planned values copied, actuals reset)
- Budgets must be created in order (can't create August 2026 without July 2026 existing)
- New users start with the current month
- Future months can be created and set up in advance
- Past months are fully editable (planned amounts, transaction assignments)
- Data is requeried each time the user navigates to a month (rollover recalculated on the fly)

## Income Line Items

- Income-ness is a property of a line item, not a group — a group is nothing but a UI grouping/sorting
  container, and any group can hold a mix of income and expense line items
- A new budget seeds an "Income" group containing one income line item ("Paycheck", planned $0) so
  there's always something for the next rule to hold onto
- A budget must always keep at least one income line item — deleting the last one is blocked
- `+ Add Item` infers whether the new line item is income from the group's existing items: an
  all-income group produces another income item, an empty group or one with any expense item
  produces an expense item (no manual income/expense toggle in the UI)
- Income line items show: planned, received, remaining
- Income does NOT roll over — fresh each month

## Groups

- User creates their own groups; the seeded "Income" group is ordinary in every respect —
  renameable, reorderable, deletable like any other
- A group's column header reads "Received" when every line item in it is income, "Spent" otherwise
  (an empty group reads "Spent")
- Groups can be reordered via drag-and-drop
- A group can only be deleted if it contains no line items

## Line Items

- Exist within a group
- Can be reordered via drag-and-drop within a group
- Cannot be moved between groups
- Each line item shows: planned (this month), spent (sum of assigned transactions), remaining (planned + rollover - spent)
- Planned amount can be $0
- Negative remaining is displayed with red text
- A line item can only be deleted if all of the following hold: it has no rollover balance carried
  in from a previous month, it has no transactions (manual or synced) assigned to it this month, and
  no later month's line item points back to it via `PreviousLineItemId`; deletion is rejected
  otherwise, nothing is cascaded or unassigned
- For an income line item, deletion is also blocked if it is the last income line item in the budget

## Zero-Based Equation

- Displayed at the top of the budget view: Total Income Planned - Total Expenses Planned = Left to Budget
- Based on planned income (not received)
- Visual indicator only (green at $0, red/yellow otherwise) — does not block any actions

## Rollover

- Every expense line item rolls over unspent/overspent balances to the next month
- Rollover silently adjusts the remaining amount (not shown as a separate number on the main view)
- Balances accumulate over time (e.g., $200/mo planned, $0 spent = $1,200 after 6 months)
- Editing a past month's planned amount cascades rollover changes through all subsequent months
- Rollover history is visible in the line item activity pane (month-by-month breakdown)
- No confirmation warning when editing past months — changes cascade silently
