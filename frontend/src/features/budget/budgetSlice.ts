import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { BudgetResponse } from '../../types/budget'

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
  },
})

export const { nextMonth, prevMonth } = budgetSlice.actions
export default budgetSlice.reducer
