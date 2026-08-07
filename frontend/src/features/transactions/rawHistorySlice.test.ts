import { describe, expect, it } from 'vitest'
import type { RawHistoryEntryResponse } from '../../types/transaction'
import reducer, { clearRawHistory, fetchRawHistory } from './rawHistorySlice'

const entry: RawHistoryEntryResponse = {
  syncedAt: '2026-08-03T06:00:00Z',
  sourceType: 'SimpleFIN',
  syncRunId: 'run-3',
  rawJson: '{"id":"abc123"}',
}

const initialState = reducer(undefined, { type: '@@INIT' })

describe('rawHistorySlice', () => {
  it('records the transaction id as soon as the fetch starts', () => {
    const state = reducer(initialState, fetchRawHistory.pending('req-1', 5))
    expect(state).toEqual({ transactionId: 5, entries: [], loading: true, error: null })
  })

  it('drops the previous transaction entries when a new fetch starts', () => {
    const loaded = reducer(
      reducer(initialState, fetchRawHistory.pending('req-1', 5)),
      fetchRawHistory.fulfilled([entry], 'req-1', 5),
    )
    const state = reducer(loaded, fetchRawHistory.pending('req-2', 6))
    expect(state.transactionId).toBe(6)
    expect(state.entries).toEqual([])
  })

  it('stores the entries the endpoint returned', () => {
    const state = reducer(
      reducer(initialState, fetchRawHistory.pending('req-1', 5)),
      fetchRawHistory.fulfilled([entry], 'req-1', 5),
    )
    expect(state).toEqual({ transactionId: 5, entries: [entry], loading: false, error: null })
  })

  it('keeps the rejected message, and the id, so the tab reports the failure once', () => {
    const state = reducer(
      reducer(initialState, fetchRawHistory.pending('req-1', 5)),
      fetchRawHistory.rejected(null, 'req-1', 5, 'The sync archive is unavailable.'),
    )
    expect(state.loading).toBe(false)
    expect(state.entries).toEqual([])
    expect(state.error).toBe('The sync archive is unavailable.')
    expect(state.transactionId).toBe(5)
  })

  it('resets everything on clear so a re-opened dialog starts blank', () => {
    const loaded = reducer(
      reducer(initialState, fetchRawHistory.pending('req-1', 5)),
      fetchRawHistory.fulfilled([entry], 'req-1', 5),
    )
    expect(reducer(loaded, clearRawHistory())).toEqual(initialState)
  })
})
