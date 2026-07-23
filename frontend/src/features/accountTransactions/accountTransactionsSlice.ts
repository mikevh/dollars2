import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { AccountTransactions } from '../../types/accountTransactions'

interface AccountTransactionsState {
  data: AccountTransactions | null
  loading: boolean
  error: string | null
}

const initialState: AccountTransactionsState = {
  data: null,
  loading: false,
  error: null,
}

export const fetchAccountTransactions = createAsyncThunk(
  'accountTransactions/fetch',
  async (accountId: number, { rejectWithValue }) => {
    const result = await api.get<AccountTransactions>(`/api/transactions/by-account/${accountId}`)
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
      state.error = null
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchAccountTransactions.pending, (state) => {
        state.loading = true
        state.error = null
      })
      .addCase(fetchAccountTransactions.fulfilled, (state, action) => {
        state.loading = false
        state.data = action.payload
      })
      .addCase(fetchAccountTransactions.rejected, (state, action) => {
        state.loading = false
        state.data = null
        state.error = action.payload as string
      })
  },
})

export const { clearAccountTransactions } = accountTransactionsSlice.actions
export default accountTransactionsSlice.reducer
