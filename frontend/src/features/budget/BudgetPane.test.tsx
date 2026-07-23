import { render, screen, fireEvent } from '@testing-library/react'
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
        isIncome: true,
        sortOrder: 0,
        lineItems: [
          {
            id: 100,
            name: 'Paycheck',
            plannedAmount: 4000,
            spentAmount: 0,
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
        isIncome: false,
        sortOrder: 1,
        lineItems: [
          {
            id: 200,
            name: 'Rent',
            plannedAmount: 1500,
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

  it('renders the "Left to budget" line with income minus expenses', () => {
    renderPane(makeBudget())
    expect(screen.getByText('Left to budget')).toBeInTheDocument()
    // 4000 income - 1500 planned expenses = 2500 left, in accent text
    const amount = screen.getByText('$2,500.00')
    expect(amount.className).toContain('text-accent-700')
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

  it('renders each group as a block with its three column labels', () => {
    renderPane(makeBudget())
    expect(screen.getByText('Housing')).toBeInTheDocument()
    // Income group uses "Received"; expense group uses "Spent"; both share "Planned"/"Remaining"
    expect(screen.getAllByText('Planned')).toHaveLength(2)
    expect(screen.getByText('Received')).toBeInTheDocument()
    expect(screen.getByText('Spent')).toBeInTheDocument()
    expect(screen.getAllByText('Remaining')).toHaveLength(2)
  })

  it('renders a negative remaining amount in accent-red', () => {
    renderPane(makeBudget())
    // Rent: planned 1500 + rollover 0 - spent 1600 = -100 remaining
    const remaining = screen.getByText('-$100.00')
    expect(remaining.className).toContain('text-accent-700')
  })

  it('reveals the group-name input when "+ Add Group" is clicked', () => {
    renderPane(makeBudget())
    fireEvent.click(screen.getByRole('button', { name: '+ Add Group' }))
    expect(screen.getByPlaceholderText('Group name')).toBeInTheDocument()
  })

  // budgetTotal for makeBudget() = income (4000+0-0) + housing (1500+0-1600) = 3900.
  it('renders "Budget vs. accounts" as $0 in calm text when accounts match the budget', () => {
    const budget = makeBudget()
    budget.accountBalanceTotal = 3900 // equal to budgetTotal → $0 difference
    renderPane(budget)
    const label = screen.getByText('Budget vs. accounts')
    const amount = label.nextElementSibling as HTMLElement
    expect(amount).toHaveTextContent('$0.00')
    expect(amount.className).toContain('text-text')
    expect(amount.className).not.toContain('text-accent-700')
  })

  it('renders the "Budget vs. accounts" difference in accent text when they differ', () => {
    const budget = makeBudget()
    budget.accountBalanceTotal = 5000 // 5000 - 3900 = 1100 difference
    renderPane(budget)
    const label = screen.getByText('Budget vs. accounts')
    const amount = label.nextElementSibling as HTMLElement
    expect(amount).toHaveTextContent('$1,100.00')
    expect(amount.className).toContain('text-accent-700')
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
})
