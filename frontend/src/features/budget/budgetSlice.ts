import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import { addStaleGuardedThunkCases } from '../../app/asyncThunkHelpers'
import type { BudgetResponse, BudgetGroupResponse, LineItemResponse } from '../../types/budget'

interface BudgetState {
  budget: BudgetResponse | null
  loading: boolean
  error: string | null
  currentYear: number
  currentMonth: number
  currentRequestId: string | null
  // Tracks which month a create is in flight for — a create started for one month must
  // not show as "in progress" for a different month navigated to in the meantime, and
  // must not overwrite that other month's already-loaded budget when it settles.
  creating: { year: number; month: number } | null
  createRequestId: string | null
}

function currentYearMonth() {
  const now = new Date()
  return { year: now.getFullYear(), month: now.getMonth() + 1 }
}

const { year: initYear, month: initMonth } = currentYearMonth()

const initialState: BudgetState = {
  budget: null,
  loading: false,
  error: null,
  currentYear: initYear,
  currentMonth: initMonth,
  currentRequestId: null,
  creating: null,
  createRequestId: null,
}

export const fetchBudget = createAsyncThunk(
  'budget/fetch',
  async ({ year, month }: { year: number; month: number }, { rejectWithValue }) => {
    const result = await api.get<BudgetResponse>(`/api/budgets/${year}/${month}`)
    if (result.error) {
      return rejectWithValue(result.error.code)
    }
    return result.data!
  }
)

export const createBudget = createAsyncThunk(
  'budget/create',
  async ({ year, month }: { year: number; month: number }, { rejectWithValue }) => {
    const result = await api.post<BudgetResponse>('/api/budgets', { year, month })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const createGroup = createAsyncThunk(
  'budget/createGroup',
  async ({ budgetId, name }: { budgetId: number; name: string }, { rejectWithValue }) => {
    const result = await api.post<BudgetGroupResponse>(`/api/budgets/${budgetId}/groups`, { name })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const updateGroup = createAsyncThunk(
  'budget/updateGroup',
  async ({ groupId, name }: { groupId: number; name: string }, { rejectWithValue }) => {
    const result = await api.put<BudgetGroupResponse>(`/api/groups/${groupId}`, { name })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const deleteGroup = createAsyncThunk(
  'budget/deleteGroup',
  async ({ groupId }: { groupId: number }, { rejectWithValue }) => {
    const result = await api.delete<boolean>(`/api/groups/${groupId}`)
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return groupId
  }
)

export const createLineItem = createAsyncThunk(
  'budget/createLineItem',
  async ({ groupId, name, plannedAmount, isIncome }: { groupId: number; name: string; plannedAmount: number; isIncome: boolean }, { rejectWithValue }) => {
    const result = await api.post<LineItemResponse>(`/api/groups/${groupId}/line-items`, { name, plannedAmount, isIncome })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return { groupId, lineItem: result.data! }
  }
)

export const updateLineItem = createAsyncThunk(
  'budget/updateLineItem',
  async ({ lineItemId, groupId, name, plannedAmount }: { lineItemId: number; groupId: number; name: string; plannedAmount: number }, { rejectWithValue }) => {
    const result = await api.put<LineItemResponse>(`/api/line-items/${lineItemId}`, { name, plannedAmount })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return { groupId, lineItem: result.data! }
  }
)

export const deleteLineItem = createAsyncThunk(
  'budget/deleteLineItem',
  async ({ lineItemId, groupId }: { lineItemId: number; groupId: number }, { rejectWithValue }) => {
    const result = await api.delete<boolean>(`/api/line-items/${lineItemId}`)
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return { groupId, lineItemId }
  }
)

export const reorderGroups = createAsyncThunk(
  'budget/reorderGroups',
  async ({ budgetId, ids }: { budgetId: number; ids: number[] }, { rejectWithValue }) => {
    const result = await api.put<boolean>(`/api/groups/reorder?budgetId=${budgetId}`, { ids })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return true
  }
)

export const reorderLineItems = createAsyncThunk(
  'budget/reorderLineItems',
  async ({ groupId, ids }: { groupId: number; ids: number[] }, { rejectWithValue }) => {
    const result = await api.put<boolean>(`/api/groups/${groupId}/line-items/reorder`, { ids })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return true
  }
)

const budgetSlice = createSlice({
  name: 'budget',
  initialState,
  reducers: {
    nextMonth(state) {
      if (state.currentMonth === 12) {
        state.currentMonth = 1
        state.currentYear += 1
      } else {
        state.currentMonth += 1
      }
    },
    prevMonth(state) {
      if (state.currentMonth === 1) {
        state.currentMonth = 12
        state.currentYear -= 1
      } else {
        state.currentMonth -= 1
      }
    },
    applyTransactionAssignment(state, action: { payload: { lineItemId: number; amount: number } }) {
      if (!state.budget) {
        return
      }
      for (const group of state.budget.groups) {
        const lineItem = group.lineItems.find((li) => li.id === action.payload.lineItemId)
        if (lineItem) {
          // Both columns move on every assignment, whatever the sign — spent is the negated net,
          // received is the net. Splitting by sign is what dropped credits on expense items.
          lineItem.spentAmount -= action.payload.amount
          lineItem.receivedAmount += action.payload.amount
          break
        }
      }
    },
    reorderGroupsLocally(state, action: { payload: { ids: number[] } }) {
      if (!state.budget) {
        return
      }
      const byId = new Map(state.budget.groups.map((g) => [g.id, g]))
      const reordered = action.payload.ids.map((id) => byId.get(id)).filter((g) => g !== undefined)
      // A concurrent fetchBudget landing between the drag ending and this dispatch could
      // replace state.budget with a different set of groups — skip rather than drop groups.
      if (reordered.length !== state.budget.groups.length) {
        return
      }
      state.budget.groups = reordered
    },
    reorderLineItemsLocally(state, action: { payload: { groupId: number; ids: number[] } }) {
      if (!state.budget) {
        return
      }
      const group = state.budget.groups.find((g) => g.id === action.payload.groupId)
      if (!group) {
        return
      }
      const byId = new Map(group.lineItems.map((li) => [li.id, li]))
      const reordered = action.payload.ids.map((id) => byId.get(id)).filter((li) => li !== undefined)
      if (reordered.length !== group.lineItems.length) {
        return
      }
      group.lineItems = reordered
    },
  },
  extraReducers: (builder) => {
    addStaleGuardedThunkCases(builder, fetchBudget, {
      onFulfilled: (state, payload) => {
        state.budget = payload
      },
    })
    // createBudget tracks its own createRequestId/creating pair, fully separate from
    // fetchBudget's currentRequestId/loading/error. A concurrent fetchBudget (e.g. a
    // TransactionPane mutation) still legitimately updates its own loading/error when
    // it settles — that alone would re-show the "Create Budget" empty state mid-create,
    // so BudgetPage must also gate that render on `creating`, not just `loading`.
    // `creating` carries the {year, month} it was started for so a create left in
    // flight while the user navigates to a different month neither blocks that
    // month's UI nor overwrites its already-loaded budget when the create settles.
    // See issue #275.
    builder
      .addCase(createBudget.pending, (state, action) => {
        state.createRequestId = action.meta.requestId
        state.creating = { year: action.meta.arg.year, month: action.meta.arg.month }
      })
      .addCase(createBudget.fulfilled, (state, action) => {
        if (action.meta.requestId !== state.createRequestId) {
          return
        }
        state.creating = null
        if (action.meta.arg.year === state.currentYear && action.meta.arg.month === state.currentMonth) {
          state.budget = action.payload
          state.error = null
          // Reclaim currentRequestId too: a fetchBudget for this same month dispatched
          // before the create started (e.g. via TransactionPane) may still be in
          // flight. Without this, its now-stale rejection landing after a successful
          // create would still pass fetchBudget's own guard (its requestId still
          // matches the untouched currentRequestId) and overwrite state.error back to
          // a stale value — masked in the UI today only because state.budget stays
          // correct, but a real inconsistency. Mirrors what the removed
          // unconditionalFulfilled reclaim used to prevent. See issue #275.
          state.currentRequestId = action.meta.requestId
        }
      })
      .addCase(createBudget.rejected, (state, action) => {
        if (action.meta.requestId !== state.createRequestId) {
          return
        }
        state.creating = null
        // Restore the actual failure reason (e.g. BUDGET_EXISTS from a raced double
        // create, not just a generic "not found") rather than leaving whatever error
        // value happened to be in state, but only if that month is still the one
        // being viewed. BudgetPage.handleCreateBudget also toasts this same payload
        // from the dispatch result directly.
        if (action.meta.arg.year === state.currentYear && action.meta.arg.month === state.currentMonth) {
          state.error = action.payload as string
        }
      })
    builder
      .addCase(createGroup.fulfilled, (state, action) => {
        if (state.budget) {
          state.budget.groups.push(action.payload)
        }
      })
      .addCase(updateGroup.fulfilled, (state, action) => {
        if (state.budget) {
          const idx = state.budget.groups.findIndex((g) => g.id === action.payload.id)
          if (idx !== -1) {
            state.budget.groups[idx] = action.payload
          }
        }
      })
      .addCase(deleteGroup.fulfilled, (state, action) => {
        if (state.budget) {
          state.budget.groups = state.budget.groups.filter((g) => g.id !== action.payload)
        }
      })
      .addCase(createLineItem.fulfilled, (state, action) => {
        if (state.budget) {
          const group = state.budget.groups.find((g) => g.id === action.payload.groupId)
          if (group) {
            group.lineItems.push(action.payload.lineItem)
          }
        }
      })
      .addCase(updateLineItem.fulfilled, (state, action) => {
        if (state.budget) {
          const group = state.budget.groups.find((g) => g.id === action.payload.groupId)
          if (group) {
            const idx = group.lineItems.findIndex((li) => li.id === action.payload.lineItem.id)
            if (idx !== -1) {
              group.lineItems[idx] = action.payload.lineItem
            }
          }
        }
      })
      .addCase(deleteLineItem.fulfilled, (state, action) => {
        if (state.budget) {
          const group = state.budget.groups.find((g) => g.id === action.payload.groupId)
          if (group) {
            group.lineItems = group.lineItems.filter((li) => li.id !== action.payload.lineItemId)
          }
        }
      })
  },
})

export const { nextMonth, prevMonth, applyTransactionAssignment, reorderGroupsLocally, reorderLineItemsLocally } = budgetSlice.actions
export default budgetSlice.reducer
