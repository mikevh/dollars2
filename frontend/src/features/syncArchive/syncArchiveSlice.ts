import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { AccountSyncArchive, SyncArchiveRun } from '../../types/syncArchive'

interface SyncArchiveState {
  /** Which account `runs` describes. Set the moment a fresh (non-paged) fetch starts, so a response
   * that lands after the viewer has switched accounts has somewhere to be recognized as stale. */
  accountId: number | null
  runs: SyncArchiveRun[]
  nextBefore: string | null
  /** True only for the first page of an account — a full-page "Loading..." state. */
  loading: boolean
  /** True while paging backwards — keeps "Load more" from being fired twice by a rapid double click. */
  loadingMore: boolean
  error: string | null
}

const initialState: SyncArchiveState = {
  accountId: null,
  runs: [],
  nextBefore: null,
  loading: false,
  loadingMore: false,
  error: null,
}

export interface FetchSyncArchiveArgs {
  accountId: number
  /** Cursor from the previous page's `nextBefore`. Omit for the first page. */
  before?: string
}

/** Runs come back newest-first; the endpoint owns that ordering. */
export const fetchSyncArchive = createAsyncThunk(
  'syncArchive/fetch',
  async ({ accountId, before }: FetchSyncArchiveArgs, { rejectWithValue }) => {
    const params = new URLSearchParams()
    if (before) {
      params.set('before', before)
    }
    const qs = params.toString()
    const result = await api.get<AccountSyncArchive>(
      `/api/accounts/${accountId}/sync-archive${qs ? `?${qs}` : ''}`
    )
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

const syncArchiveSlice = createSlice({
  name: 'syncArchive',
  initialState,
  reducers: {
    clearSyncArchive: () => initialState,
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchSyncArchive.pending, (state, action) => {
        state.error = null
        if (action.meta.arg.before) {
          state.loadingMore = true
        } else {
          // A fresh load (no cursor) starts the account over, dropping whatever an in-flight
          // "load more" for a previous account left behind.
          state.accountId = action.meta.arg.accountId
          state.runs = []
          state.nextBefore = null
          state.loading = true
          state.loadingMore = false
        }
      })
      .addCase(fetchSyncArchive.fulfilled, (state, action) => {
        // A response only belongs on screen if it's still describing the account currently being
        // viewed — the account can switch while this was in flight.
        if (action.meta.arg.accountId !== state.accountId) {
          return
        }
        state.loading = false
        state.loadingMore = false
        state.runs = action.meta.arg.before ? [...state.runs, ...action.payload.runs] : action.payload.runs
        state.nextBefore = action.payload.nextBefore
      })
      .addCase(fetchSyncArchive.rejected, (state, action) => {
        if (action.meta.arg.accountId !== state.accountId) {
          return
        }
        state.loading = false
        state.loadingMore = false
        // api.get resolves its envelope rather than throwing, so the rejection always carries a
        // message — the fallback only covers a thunk aborted before it ever ran. `||` rather than
        // `??` so an (unexpected) empty-string message still falls back instead of leaving the
        // error state falsy, which would silently read as the empty "never synced" state.
        state.error = (action.payload as string) || 'Could not load the sync archive.'
      })
  },
})

export const { clearSyncArchive } = syncArchiveSlice.actions
export default syncArchiveSlice.reducer
