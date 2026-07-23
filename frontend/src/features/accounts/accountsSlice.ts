import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { AccountGroup } from '../../types/account'

interface AccountsState {
  groups: AccountGroup[]
  loading: boolean
  error: string | null
}

const initialState: AccountsState = {
  groups: [],
  loading: false,
  error: null,
}

export const fetchAccounts = createAsyncThunk(
  'accounts/fetch',
  async (_, { rejectWithValue }) => {
    const result = await api.get<AccountGroup[]>('/api/accounts')
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

const accountsSlice = createSlice({
  name: 'accounts',
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchAccounts.pending, (state) => {
        state.loading = true
        state.error = null
      })
      .addCase(fetchAccounts.fulfilled, (state, action) => {
        state.loading = false
        state.groups = action.payload
      })
      .addCase(fetchAccounts.rejected, (state, action) => {
        state.loading = false
        state.error = action.payload as string
      })
  },
})

export default accountsSlice.reducer
