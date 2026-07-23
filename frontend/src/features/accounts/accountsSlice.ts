import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { AccountGroup, SyncResult } from '../../types/account'

interface AccountsState {
  groups: AccountGroup[]
  loading: boolean
  error: string | null
  /** connectionId of the group currently syncing, or null. Only one syncs at a time (server lock). */
  syncingConnectionId: string | null
}

const initialState: AccountsState = {
  groups: [],
  loading: false,
  error: null,
  syncingConnectionId: null,
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

export const syncConnection = createAsyncThunk(
  'accounts/syncConnection',
  async (connectionId: string, { dispatch, rejectWithValue }) => {
    const result = await api.post<SyncResult[]>(`/api/sync/connection/${connectionId}`)
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    // Refresh last-sync/balance for the synced accounts.
    await dispatch(fetchAccounts())
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
      .addCase(syncConnection.pending, (state, action) => {
        state.syncingConnectionId = action.meta.arg
      })
      .addCase(syncConnection.fulfilled, (state) => {
        state.syncingConnectionId = null
      })
      .addCase(syncConnection.rejected, (state) => {
        state.syncingConnectionId = null
      })
  },
})

export default accountsSlice.reducer
