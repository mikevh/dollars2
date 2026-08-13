import { describe, expect, it } from 'vitest'
import type { AccountSyncArchive, SyncArchiveRun } from '../../types/syncArchive'
import reducer, { clearSyncArchive, fetchSyncArchive } from './syncArchiveSlice'

const run = (overrides: Partial<SyncArchiveRun> = {}): SyncArchiveRun => ({
  syncRunId: 'run-3',
  syncedAt: '2026-08-03T06:00:00Z',
  sourceType: 'SimpleFIN',
  transactionCount: 1,
  removedCount: 0,
  errorCount: 0,
  skippedCount: 0,
  accountMetadataJson: null,
  items: [],
  ...overrides,
})

const archivePage = (
  runs: SyncArchiveRun[],
  nextBefore: string | null = null
): AccountSyncArchive => ({
  accountName: 'Chase Checking',
  sourceType: 'SimpleFIN',
  runs,
  nextBefore,
})

const initialState = reducer(undefined, { type: '@@INIT' })

describe('syncArchiveSlice', () => {
  it('records the account id and clears prior runs when a fresh page starts', () => {
    const state = reducer(initialState, fetchSyncArchive.pending('req-1', { accountId: 5 }))
    expect(state).toEqual({
      accountId: 5,
      accountName: '',
      sourceType: '',
      runs: [],
      nextBefore: null,
      loading: true,
      loadingMore: false,
      error: null,
    })
  })

  it('marks loadingMore, not loading, when the page carries a cursor', () => {
    const loaded = reducer(
      reducer(initialState, fetchSyncArchive.pending('req-1', { accountId: 5 })),
      fetchSyncArchive.fulfilled(archivePage([run()], '2026-08-02T06:00:00Z'), 'req-1', { accountId: 5 }),
    )
    const state = reducer(
      loaded,
      fetchSyncArchive.pending('req-2', { accountId: 5, before: '2026-08-02T06:00:00Z' }),
    )
    expect(state.loading).toBe(false)
    expect(state.loadingMore).toBe(true)
    // A "load more" page must not clear what is already on screen.
    expect(state.runs).toEqual([run()])
  })

  it('replaces runs on a fresh page but appends on a load-more page', () => {
    const first = reducer(
      reducer(initialState, fetchSyncArchive.pending('req-1', { accountId: 5 })),
      fetchSyncArchive.fulfilled(
        archivePage([run({ syncRunId: 'run-2' })], '2026-08-02T06:00:00Z'),
        'req-1',
        { accountId: 5 },
      ),
    )
    const more = reducer(
      reducer(first, fetchSyncArchive.pending('req-2', { accountId: 5, before: '2026-08-02T06:00:00Z' })),
      fetchSyncArchive.fulfilled(
        archivePage([run({ syncRunId: 'run-1' })], null),
        'req-2',
        { accountId: 5, before: '2026-08-02T06:00:00Z' },
      ),
    )
    expect(more.runs.map((r) => r.syncRunId)).toEqual(['run-2', 'run-1'])
    expect(more.nextBefore).toBeNull()
    expect(more.loadingMore).toBe(false)
  })

  it('ignores a fulfilled response for an account that is no longer being viewed', () => {
    const forFive = reducer(initialState, fetchSyncArchive.pending('req-1', { accountId: 5 }))
    // Account switched (a fresh mount dispatched its own pending) before the first request landed.
    const switched = reducer(forFive, fetchSyncArchive.pending('req-2', { accountId: 7 }))
    const state = reducer(
      switched,
      fetchSyncArchive.fulfilled(archivePage([run()], null), 'req-1', { accountId: 5 }),
    )
    expect(state.accountId).toBe(7)
    expect(state.runs).toEqual([])
  })

  it('ignores a rejected response for an account that is no longer being viewed', () => {
    const forFive = reducer(initialState, fetchSyncArchive.pending('req-1', { accountId: 5 }))
    const switched = reducer(forFive, fetchSyncArchive.pending('req-2', { accountId: 7 }))
    const state = reducer(
      switched,
      fetchSyncArchive.rejected(null, 'req-1', { accountId: 5 }, 'The sync archive is unavailable.'),
    )
    expect(state.accountId).toBe(7)
    expect(state.error).toBeNull()
  })

  it('keeps the rejected message and stops both loading flags', () => {
    const state = reducer(
      reducer(initialState, fetchSyncArchive.pending('req-1', { accountId: 5 })),
      fetchSyncArchive.rejected(null, 'req-1', { accountId: 5 }, 'Account not found.'),
    )
    expect(state.loading).toBe(false)
    expect(state.loadingMore).toBe(false)
    expect(state.error).toBe('Account not found.')
  })

  it('resets everything on clear so switching back to this account starts blank', () => {
    const loaded = reducer(
      reducer(initialState, fetchSyncArchive.pending('req-1', { accountId: 5 })),
      fetchSyncArchive.fulfilled(archivePage([run()], null), 'req-1', { accountId: 5 }),
    )
    expect(reducer(loaded, clearSyncArchive())).toEqual(initialState)
  })
})
