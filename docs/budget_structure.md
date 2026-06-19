---
name: budget-structure
description: Monthly zero-based budgets with groups, line items, rollover balances, and template copying
metadata:
  type: project
---

## Monthly Budget

- One budget per month per user
- Budget is created by copying the prior month's budget (planned values copied, actuals reset)
- Budgets must be created in order (can't create August 2026 without July 2026 existing)
- New users start with the current month
- Future months can be created and set up in advance
- Past months are fully editable (planned amounts, transaction assignments)
- Data is requeried each time the user navigates to a month (rollover recalculated on the fly)

## Income Group

- Fixed group pinned at the top, cannot be deleted or reordered
- User adds income line items (Paycheck 1, Paycheck 2, Side Hustle, etc.)
- Income line items show: planned, received, remaining
- Income does NOT roll over — fresh each month

## Expense Groups

- User creates their own groups (no pre-populated defaults)
- Groups can be reordered via drag-and-drop
- A group can only be deleted if it contains no line items

## Line Items

- Exist within a group
- Can be reordered via drag-and-drop within a group
- Cannot be moved between groups
- Each line item shows: planned (this month), spent (sum of assigned transactions), remaining (planned + rollover - spent)
- Planned amount can be $0
- Negative remaining is displayed with red text
- A line item can only be deleted if it has zero balance and no synced transactions assigned to it

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
