import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { BudgetResponse, BudgetGroupResponse, LineItemResponse } from '../../types/budget'

interface BudgetState {
  budget: BudgetResponse | null
  loading: boolean
  error: string | null
  currentYear: number
  currentMonth: number
}

const now = new Date()

const initialState: BudgetState = {
  budget: null,
  loading: false,
  error: null,
  currentYear: now.getFullYear(),
  currentMonth: now.getMonth() + 1,
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
  async ({ groupId, name, plannedAmount }: { groupId: number; name: string; plannedAmount: number }, { rejectWithValue }) => {
    const result = await api.post<LineItemResponse>(`/api/groups/${groupId}/line-items`, { name, plannedAmount })
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
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchBudget.pending, (state) => {
        state.loading = true
        state.error = null
        state.budget = null
      })
      .addCase(fetchBudget.fulfilled, (state, action) => {
        state.loading = false
        state.budget = action.payload
      })
      .addCase(fetchBudget.rejected, (state, action) => {
        state.loading = false
        state.error = action.payload as string
      })
      .addCase(createBudget.pending, (state) => {
        state.loading = true
        state.error = null
      })
      .addCase(createBudget.fulfilled, (state, action) => {
        state.loading = false
        state.budget = action.payload
      })
      .addCase(createBudget.rejected, (state, action) => {
        state.loading = false
        state.error = action.payload as string
      })
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

export const { nextMonth, prevMonth } = budgetSlice.actions
export default budgetSlice.reducer
