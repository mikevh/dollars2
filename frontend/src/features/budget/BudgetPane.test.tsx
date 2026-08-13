import { render, screen, fireEvent, within } from '@testing-library/react'
import { Provider } from 'react-redux'
import { DndContext } from '@dnd-kit/core'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { store } from '../../app/store'
import type { BudgetResponse } from '../../types/budget'
import BudgetPane from './BudgetPane'

function makeBudget(): BudgetResponse {
  return {
    id: 1,
    year: 2026,
    month: 7,
    accountBalanceTotal: 0,
    groups: [
      {
        id: 10,
        name: 'Income',
        sortOrder: 0,
        lineItems: [
          {
            id: 100,
            name: 'Paycheck',
            plannedAmount: 4000,
            isIncome: true,
            // spentAmount is the negated net of a line item's assignments, so on an income item it
            // mirrors receivedAmount. "Budget vs. accounts" must not pick this up (issue #71).
            spentAmount: -4000,
            receivedAmount: 4000,
            rolloverAmount: 0,
            sortOrder: 0,
            notes: null,
          },
        ],
      },
      {
        id: 20,
        name: 'Housing',
        sortOrder: 1,
        lineItems: [
          {
            id: 200,
            name: 'Rent',
            plannedAmount: 1500,
            isIncome: false,
            spentAmount: 1600,
            receivedAmount: 0,
            rolloverAmount: 0,
            sortOrder: 0,
            notes: null,
          },
        ],
      },
    ],
  }
}

function renderPane(budget: BudgetResponse) {
  return render(
    <Provider store={store}>
      <DndContext>
        <BudgetPane budget={budget} />
      </DndContext>
    </Provider>,
  )
}

describe('BudgetPane (Modernist restyle)', () => {
  // makeBudget() is July 2026; pin the clock so the current-month "Budget vs. accounts"
  // row renders deterministically regardless of the wall clock.
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-15T12:00:00'))
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows the left-to-budget amount in calm (non-accent) text when balanced', () => {
    const budget = makeBudget()
    budget.groups[1].lineItems[0].plannedAmount = 4000 // expenses == income → $0 left
    budget.groups[0].lineItems[0].receivedAmount = 3000 // keep income line remaining non-zero
    renderPane(budget)
    const amount = screen.getByText('$0.00')
    expect(amount.className).toContain('text-text')
    expect(amount.className).not.toContain('text-accent-700')
  })

  it('renders each group as a block with its column labels and metric dropdown', () => {
    renderPane(makeBudget())
    expect(screen.getByText('Housing')).toBeInTheDocument()
    // Both groups share "Planned"/"Remaining"; "Remaining" is the dropdown's default selection.
    expect(screen.getAllByText('Planned')).toHaveLength(2)
    expect(screen.getAllByText('Remaining')).toHaveLength(2)

    // A group whose items are all income offers "Received" as the dropdown alternative; otherwise "Spent".
    const incomeCard = screen.getByText('Income').closest('.card') as HTMLElement
    fireEvent.click(within(incomeCard).getByRole('button', { name: /Remaining/ }))
    expect(within(incomeCard).getByText('Received')).toBeInTheDocument()

    const housingCard = screen.getByText('Housing').closest('.card') as HTMLElement
    fireEvent.click(within(housingCard).getByRole('button', { name: /Remaining/ }))
    expect(within(housingCard).getByText('Spent')).toBeInTheDocument()
  })

  it('renders a negative remaining amount in accent-red', () => {
    renderPane(makeBudget())
    // Rent: planned 1500 + rollover 0 - spent 1600 = -100 remaining, shown in the row (accent-red)
    // and again in the group footer total (plain — the footer isn't status-colored).
    const matches = screen.getAllByText('-$100.00')
    const remaining = matches.find((el) => el.className.includes('text-accent-700'))
    expect(remaining).toBeDefined()
  })

  it('reveals the group-name input when "+ Add Group" is clicked', () => {
    renderPane(makeBudget())
    fireEvent.click(screen.getByRole('button', { name: '+ Add Group' }))
    expect(screen.getByPlaceholderText('Group name')).toBeInTheDocument()
  })

  it('hides the "Budget vs. accounts" row when viewing a past month', () => {
    const budget = makeBudget()
    budget.month = 6 // current month is 2026-07 → June is past
    renderPane(budget)
    expect(screen.queryByText('Budget vs. accounts')).not.toBeInTheDocument()
  })

  it('hides the "Budget vs. accounts" row when viewing a future month', () => {
    const budget = makeBudget()
    budget.month = 8 // current month is 2026-07 → August is future
    renderPane(budget)
    expect(screen.queryByText('Budget vs. accounts')).not.toBeInTheDocument()
  })

  // Issue #71: a positive (income) transaction assigned to an expense line item is real spend
  // activity — it arrives as a negative spentAmount and must raise Remaining by its full amount.
  // Issue #80: that inflow renders in the Spent column as a green, +signed credit (not a bare negative).
  it('renders a credit-heavy expense item with a green +Spent credit and raised Remaining', () => {
    const budget = makeBudget()
    const gifts = budget.groups[1].lineItems[0]
    gifts.name = 'Gifts'
    gifts.plannedAmount = 300
    gifts.spentAmount = -690.89 // one +$690.89 assignment
    renderPane(budget)
    // 300 + 0 - (-690.89) = 990.89, shown by default (Remaining is the dropdown's default metric).
    expect(screen.getAllByText('$990.89')[0]).toBeInTheDocument()

    // Switch Housing's metric to Spent to see the signed credit.
    const housingCard = screen.getByText('Gifts').closest('.card') as HTMLElement
    fireEvent.click(within(housingCard).getByRole('button', { name: /Remaining/ }))
    fireEvent.click(within(housingCard).getByRole('menuitem', { name: 'Spent' }))

    const spent = screen.getByText('+$690.89')
    expect(spent.className).toContain('text-positive')
    expect(spent.className).not.toContain('text-accent-700')
  })
})
