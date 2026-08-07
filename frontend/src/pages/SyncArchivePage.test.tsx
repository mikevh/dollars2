import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Provider } from 'react-redux'
import { configureStore } from '@reduxjs/toolkit'
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom'
import toast from 'react-hot-toast'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import syncArchiveReducer from '../features/syncArchive/syncArchiveSlice'
import accountsReducer from '../features/accounts/accountsSlice'
import type { AccountGroup } from '../types/account'
import type { AccountSyncArchive, SyncArchiveRun } from '../types/syncArchive'
import SyncArchivePage from './SyncArchivePage'

const getMock = vi.fn()
// The page fetches both /api/accounts (for the name and sourceType) and the sync-archive endpoint;
// routed separately so one test's mock of the archive page doesn't have to also shape an accounts
// response, and vice versa.
const accountsGetMock = vi.fn()
vi.mock('../api/client', () => ({
  api: {
    get: (endpoint: string) => (endpoint === '/api/accounts' ? accountsGetMock(endpoint) : getMock(endpoint)),
  },
}))

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}))

function accountGroup(overrides: Partial<AccountGroup> = {}): AccountGroup {
  return {
    connectionId: 'conn-1',
    sourceType: 'SimpleFIN',
    accounts: [{ id: 3, name: 'Chase Checking', lastSyncedAt: null, lastStatus: null, balance: null }],
    ...overrides,
  }
}

function run(overrides: Partial<SyncArchiveRun> = {}): SyncArchiveRun {
  return {
    syncRunId: 'run-2',
    syncedAt: '2026-08-03T06:00:00Z',
    sourceType: 'SimpleFIN',
    transactionCount: 1,
    removedCount: 0,
    errorCount: 0,
    skippedCount: 0,
    accountMetadataJson: null,
    items: [
      { itemType: 'Transaction', providerTransactionId: 'txn-1', rawJson: '{"amount":"-42.10"}' },
    ],
    ...overrides,
  }
}

function archivePage(runs: SyncArchiveRun[], nextBefore: string | null = null): AccountSyncArchive {
  return { runs, nextBefore }
}

function buildStore() {
  return configureStore({ reducer: { syncArchive: syncArchiveReducer, accounts: accountsReducer } })
}

function renderPage(accountId = '3') {
  const store = buildStore()
  return render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/accounts/${accountId}/sync-archive`]}>
        <Routes>
          <Route path="/accounts/:accountId/sync-archive" element={<SyncArchivePage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  )
}

function renderPageWithSwitcher(accountId: string, nextAccountId: string) {
  const store = buildStore()
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/accounts/${accountId}/sync-archive`]}>
        <Link to={`/accounts/${nextAccountId}/sync-archive`}>Switch account</Link>
        <Routes>
          <Route path="/accounts/:accountId/sync-archive" element={<SyncArchivePage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  )
}

describe('SyncArchivePage', () => {
  beforeEach(() => {
    getMock.mockReset()
    accountsGetMock.mockReset()
    accountsGetMock.mockResolvedValue({ data: [accountGroup()], error: null })
    vi.mocked(toast.error).mockClear()
  })

  it('requests the first page for the account in the route', async () => {
    getMock.mockResolvedValue({ data: archivePage([]), error: null })
    renderPage('3')
    await waitFor(() => expect(getMock).toHaveBeenCalledWith('/api/accounts/3/sync-archive'))
  })

  it('shows the account name in the header', async () => {
    getMock.mockResolvedValue({ data: archivePage([]), error: null })
    renderPage('3')
    expect(await screen.findByRole('heading', { name: 'Chase Checking — Sync Archive' })).toBeInTheDocument()
  })

  it('lists runs newest-first as the endpoint returns them', async () => {
    getMock.mockResolvedValue({
      data: archivePage([
        run({ syncRunId: 'run-2', syncedAt: '2026-08-03T06:00:00Z' }),
        run({ syncRunId: 'run-1', syncedAt: '2026-08-02T06:00:00Z' }),
      ]),
      error: null,
    })
    renderPage()

    const rows = await screen.findAllByRole('button', { expanded: false })
    expect(rows.map((r) => r.textContent)).toEqual([
      expect.stringContaining('2026-08-03 06:00 UTC'),
      expect.stringContaining('2026-08-02 06:00 UTC'),
    ])
  })

  it('marks a run with provider errors', async () => {
    getMock.mockResolvedValue({
      data: archivePage([run({ errorCount: 1, items: [{ itemType: 'ProviderError', providerTransactionId: null, rawJson: '{"message":"boom"}' }] })]),
      error: null,
    })
    renderPage()

    await screen.findByText('1 error')
  })

  it('expands a run to show its metadata, transaction, removed, and error items with readable JSON', async () => {
    getMock.mockResolvedValue({
      data: archivePage([
        run({
          transactionCount: 1,
          removedCount: 1,
          errorCount: 1,
          accountMetadataJson: '{"balance":"1204.55"}',
          items: [
            { itemType: 'Transaction', providerTransactionId: 'txn-1', rawJson: '{"amount":"-42.10"}' },
            { itemType: 'Removed', providerTransactionId: 'txn-old', rawJson: null },
            { itemType: 'ProviderError', providerTransactionId: null, rawJson: '{"message":"boom"}' },
          ],
        }),
      ]),
      error: null,
    })
    renderPage()

    const runToggle = await screen.findByText('SimpleFIN')
    fireEvent.click(runToggle.closest('button')!)

    // Metadata item, expandable to its own JSON.
    fireEvent.click(screen.getByText('Account metadata'))
    expect(screen.getByText(/"balance": "1204.55"/)).toBeInTheDocument()

    // Transaction item, expandable to its own JSON.
    fireEvent.click(screen.getByText('txn-1'))
    expect(screen.getByText(/"amount": "-42.10"/)).toBeInTheDocument()

    // Removed item has no JSON — just the id, not a disclosure.
    expect(screen.getByText('txn-old')).toBeInTheDocument()

    // Error item, expandable to its own JSON.
    fireEvent.click(screen.getByText('Error #1'))
    expect(screen.getByText(/"message": "boom"/)).toBeInTheDocument()
  })

  it('pages backwards with the previous nextBefore and appends without replacing', async () => {
    getMock.mockResolvedValueOnce({
      data: archivePage([run({ syncRunId: 'run-2', syncedAt: '2026-08-03T06:00:00Z' })], '2026-08-02T06:00:00Z'),
      error: null,
    })
    renderPage()
    await screen.findByText('SimpleFIN')

    getMock.mockResolvedValueOnce({
      data: archivePage([run({ syncRunId: 'run-1', syncedAt: '2026-08-01T06:00:00Z' })], null),
      error: null,
    })
    fireEvent.click(screen.getByRole('button', { name: 'Load more' }))

    await waitFor(() =>
      expect(getMock).toHaveBeenLastCalledWith('/api/accounts/3/sync-archive?before=2026-08-02T06%3A00%3A00Z'),
    )
    // Both runs are on screen — the older page appended rather than replaced.
    const rows = await screen.findAllByRole('button', { expanded: false })
    expect(rows.length).toBe(2)
    // The button disappears once nextBefore comes back null.
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull())
  })

  it('disables Load more while a page is in flight, so a rapid double click fires one request', async () => {
    getMock.mockResolvedValueOnce({
      data: archivePage([run()], '2026-08-02T06:00:00Z'),
      error: null,
    })
    renderPage()
    await screen.findByText('SimpleFIN')

    let resolveNext!: (value: { data: AccountSyncArchive; error: null }) => void
    getMock.mockImplementationOnce(
      () => new Promise<{ data: AccountSyncArchive; error: null }>((resolve) => {
        resolveNext = resolve
      }),
    )

    const loadMore = screen.getByRole('button', { name: 'Load more' })
    fireEvent.click(loadMore)
    expect(loadMore).toBeDisabled()

    fireEvent.click(loadMore)
    const callsForLoadMore = getMock.mock.calls.filter(([url]) => (url as string).includes('before='))
    expect(callsForLoadMore).toHaveLength(1)

    resolveNext({ data: archivePage([run({ syncRunId: 'run-older' })], null), error: null })
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Load more' })).toBeNull())
  })

  it('does not apply a stale response after switching to another account mid-fetch', async () => {
    let resolveAccountThree!: (value: { data: AccountSyncArchive; error: null }) => void
    getMock.mockImplementationOnce(
      () => new Promise<{ data: AccountSyncArchive; error: null }>((resolve) => {
        resolveAccountThree = resolve
      }),
    )
    renderPageWithSwitcher('3', '7')
    expect(await screen.findByText('Loading...')).toBeInTheDocument()

    // Switch to account 7 before account 3's request has resolved; account 7's own request
    // is left hanging too, so only the stale-response guard — not a later fulfillment — is
    // what keeps account 3's data off screen.
    getMock.mockImplementationOnce(() => new Promise(() => {}))
    fireEvent.click(screen.getByRole('link', { name: 'Switch account' }))
    await waitFor(() => expect(getMock).toHaveBeenLastCalledWith('/api/accounts/7/sync-archive'))

    resolveAccountThree({ data: archivePage([run({ syncRunId: 'account-3-run' })]), error: null })
    await waitFor(() => expect(screen.queryByText('SimpleFIN')).toBeNull())
    expect(screen.getByText('Loading...')).toBeInTheDocument()
  })

  it('reports loading, not an empty archive, before the fetch has been dispatched', () => {
    // The mount effect that dispatches fetchSyncArchive runs after the first paint, so the very
    // first render — before any request has even gone out — must not read as "never synced".
    getMock.mockReturnValue(new Promise(() => {}))
    renderPage()

    expect(screen.getByText('Loading...')).toBeInTheDocument()
    expect(screen.queryByText('This account has never synced.')).not.toBeInTheDocument()
  })

  it('shows an empty state, not an error, when the account has never synced', async () => {
    getMock.mockResolvedValue({ data: archivePage([]), error: null })
    renderPage()
    expect(await screen.findByText('This account has never synced.')).toBeInTheDocument()
  })

  it('explains that a manual account does not sync, reached directly by URL', async () => {
    getMock.mockResolvedValue({ data: archivePage([]), error: null })
    accountsGetMock.mockResolvedValue({ data: [accountGroup({ sourceType: 'Manual' })], error: null })
    renderPage()
    expect(
      await screen.findByText("This account doesn't sync, so there is nothing archived for it."),
    ).toBeInTheDocument()
  })

  it('shows a not-found state for another user\'s account without crashing', async () => {
    getMock.mockResolvedValue({ data: null, error: { message: 'Account not found.', code: 'ACCOUNT_NOT_FOUND' } })
    renderPage()
    expect(await screen.findByText('Account not found.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('shows an inline error with retry and a toast when the fetch fails', async () => {
    getMock.mockResolvedValue({ data: null, error: { message: 'The sync archive is unavailable.', code: 'ARCHIVE_UNAVAILABLE' } })
    renderPage()

    expect(await screen.findByText('The sync archive is unavailable.')).toBeInTheDocument()
    expect(vi.mocked(toast.error)).toHaveBeenCalledWith('The sync archive is unavailable.')

    getMock.mockResolvedValueOnce({ data: archivePage([run()]), error: null })
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    await screen.findByText('SimpleFIN')
  })

  it('renders a malformed payload verbatim instead of throwing', async () => {
    getMock.mockResolvedValue({
      data: archivePage([
        run({ items: [{ itemType: 'Transaction', providerTransactionId: 'txn-bad', rawJson: '{"amount":' }] }),
      ]),
      error: null,
    })
    renderPage()

    fireEvent.click((await screen.findByText('SimpleFIN')).closest('button')!)
    fireEvent.click(screen.getByText('txn-bad'))
    expect(screen.getByText('{"amount":')).toBeInTheDocument()
  })
})
