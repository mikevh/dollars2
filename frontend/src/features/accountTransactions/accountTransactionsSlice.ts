import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import { addStaleGuardedThunkCases } from '../../app/asyncThunkHelpers'
import type { AccountTransactions } from '../../types/accountTransactions'

interface AccountTransactionsState {
  data: AccountTransactions | null
  loading: boolean
  error: string | null
  currentRequestId: string | null
}

const initialState: AccountTransactionsState = {
  data: null,
  loading: false,
  error: null,
  currentRequestId: null,
}

export interface AccountTransactionsQuery {
  accountId: number
  page: number
  size: number
  sort: string
  dir: string
  q: string
  includeDeleted: boolean
}

export const fetchAccountTransactions = createAsyncThunk(
  'accountTransactions/fetch',
  async (
    { accountId, page, size, sort, dir, q, includeDeleted }: AccountTransactionsQuery,
    { rejectWithValue }
  ) => {
    const params = new URLSearchParams({
      page: String(page),
      size: String(size),
      sort,
      dir,
    })
    if (q) {
      params.set('q', q)
    }
    // Only sent when opted in — the backend defaults to excluding deleted rows.
    if (includeDeleted) {
      params.set('includeDeleted', 'true')
    }
    const result = await api.get<AccountTransactions>(
      `/api/transactions/by-account/${accountId}?${params.toString()}`
    )
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

const accountTransactionsSlice = createSlice({
  name: 'accountTransactions',
  initialState,
  reducers: {
    clearAccountTransactions: (state) => {
      state.data = null
      state.loading = false
      state.error = null
      state.currentRequestId = null
    },
  },
  extraReducers: (builder) => {
    addStaleGuardedThunkCases(builder, fetchAccountTransactions, {
      onFulfilled: (state, payload) => {
        state.data = payload
      },
      onRejected: (state) => {
        state.data = null
      },
    })
  },
})

export const { clearAccountTransactions } = accountTransactionsSlice.actions
export default accountTransactionsSlice.reducer
