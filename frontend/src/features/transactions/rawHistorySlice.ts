import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { RawHistoryEntryResponse } from '../../types/transaction'

interface RawHistoryState {
  /**
   * Which transaction the entries describe. Set the moment the fetch starts rather than when it
   * lands, so it doubles as the "already asked for this one" guard that keeps the tab's lazy load
   * to a single request no matter how often the user toggles back to it.
   */
  transactionId: number | null
  entries: RawHistoryEntryResponse[]
  loading: boolean
  error: string | null
}

const initialState: RawHistoryState = {
  transactionId: null,
  entries: [],
  loading: false,
  error: null,
}

/** Entries come back newest sighting first; the endpoint owns that ordering. */
export const fetchRawHistory = createAsyncThunk(
  'rawHistory/fetch',
  async (transactionId: number, { rejectWithValue }) => {
    const result = await api.get<RawHistoryEntryResponse[]>(
      `/api/transactions/${transactionId}/raw-history`
    )
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data ?? []
  }
)

const rawHistorySlice = createSlice({
  name: 'rawHistory',
  initialState,
  reducers: {
    clearRawHistory: () => initialState,
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchRawHistory.pending, (state, action) => {
        state.transactionId = action.meta.arg
        state.entries = []
        state.loading = true
        state.error = null
      })
      .addCase(fetchRawHistory.fulfilled, (state, action) => {
        state.loading = false
        state.entries = action.payload
      })
      .addCase(fetchRawHistory.rejected, (state, action) => {
        state.loading = false
        state.entries = []
        // api.get resolves its envelope rather than throwing, so the rejection always carries a
        // message — the fallback only covers a thunk aborted before it ever ran.
        state.error = (action.payload as string) ?? 'Could not load raw history.'
      })
  },
})

export const { clearRawHistory } = rawHistorySlice.actions
export default rawHistorySlice.reducer
