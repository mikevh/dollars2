import { describe, expect, it } from 'vitest'
import type { BudgetResponse } from '../../types/budget'
import budgetReducer, { fetchBudget } from './budgetSlice'

function makeBudget(): BudgetResponse {
  return {
    id: 1,
    year: 2026,
    month: 7,
    accountBalanceTotal: 0,
    groups: [],
  }
}

describe('budgetSlice fetchBudget.pending', () => {
  it('keeps the existing budget in state while a refetch is in flight', () => {
    const budget = makeBudget()
    const state = budgetReducer(
      { budget, loading: false, error: null, currentYear: 2026, currentMonth: 7 },
      fetchBudget.pending('requestId', { year: 2026, month: 7 }),
    )

    expect(state.loading).toBe(true)
    expect(state.budget).toBe(budget)
  })

  it('clears a previous error when a new fetch starts', () => {
    const state = budgetReducer(
      { budget: null, loading: false, error: 'BUDGET_NOT_FOUND', currentYear: 2026, currentMonth: 7 },
      fetchBudget.pending('requestId', { year: 2026, month: 7 }),
    )

    expect(state.error).toBeNull()
  })
})
