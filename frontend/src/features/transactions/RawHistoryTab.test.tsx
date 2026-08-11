import { render, screen, fireEvent } from '@testing-library/react'
import { Provider } from 'react-redux'
import toast from 'react-hot-toast'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { store } from '../../app/store'
import { api } from '../../api/client'
import type { RawHistoryEntryResponse } from '../../types/transaction'
import RawHistoryTab from './RawHistoryTab'
import { clearRawHistory } from './rawHistorySlice'

vi.mock('../../api/client', () => ({
  api: { get: vi.fn() },
}))

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}))

function makeEntry(overrides: Partial<RawHistoryEntryResponse> = {}): RawHistoryEntryResponse {
  return {
    syncedAt: '2026-08-03T06:00:00Z',
    sourceType: 'SimpleFIN',
    syncRunId: 'run-3',
    rawJson: '{"id":"abc123","amount":"-42.10","pending":false}',
    ...overrides,
  }
}

function renderTab(transactionId = 5, isManual = false) {
  return render(
    <Provider store={store}>
      <RawHistoryTab transactionId={transactionId} isManual={isManual} />
    </Provider>,
  )
}

describe('RawHistoryTab', () => {
  beforeEach(() => {
    store.dispatch(clearRawHistory())
    vi.mocked(api.get).mockReset()
    vi.mocked(toast.error).mockClear()
  })

  it('fetches the transaction raw history on mount', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [makeEntry()], error: null })
    renderTab(5)

    await screen.findByText('1 sighting from SimpleFIN')
    expect(vi.mocked(api.get)).toHaveBeenCalledTimes(1)
    expect(vi.mocked(api.get).mock.calls[0][0]).toBe('/api/transactions/5/raw-history')
  })

  it('renders sightings in the order returned, newest expanded and the rest collapsed', async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: [
        makeEntry({ syncedAt: '2026-08-03T06:00:00Z', syncRunId: 'run-3' }),
        makeEntry({ syncedAt: '2026-08-02T06:00:00Z', syncRunId: 'run-2', rawJson: '{"pending":true}' }),
        makeEntry({ syncedAt: '2026-08-01T06:00:00Z', syncRunId: 'run-1', rawJson: '{"pending":true}' }),
      ],
      error: null,
    })
    renderTab()

    await screen.findByText('3 sightings from SimpleFIN')

    const toggles = screen.getAllByRole('button')
    expect(toggles.map((b) => b.textContent)).toEqual([
      '▾2026-08-03 06:00 UTCposted',
      '▸2026-08-02 06:00 UTCpending',
      '▸2026-08-01 06:00 UTCpending',
    ])

    // Only the newest sighting's payload is on screen.
    expect(screen.getAllByRole('button', { expanded: true })).toHaveLength(1)
    expect(document.querySelectorAll('pre')).toHaveLength(1)
  })

  it('expands and re-collapses an older sighting when its header is clicked', async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: [makeEntry({ syncRunId: 'run-2' }), makeEntry({ syncRunId: 'run-1', rawJson: '{"older":true}' })],
      error: null,
    })
    renderTab()

    await screen.findByText('2 sightings from SimpleFIN')
    const older = screen.getAllByRole('button')[1]

    fireEvent.click(older)
    expect(screen.getByText(/"older": true/)).toBeInTheDocument()

    fireEvent.click(older)
    expect(screen.queryByText(/"older": true/)).not.toBeInTheDocument()
  })

  it('renders a malformed payload verbatim instead of throwing', async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: [makeEntry({ rawJson: '{"id":"abc123",' })],
      error: null,
    })
    renderTab()

    await screen.findByText('1 sighting from SimpleFIN')
    expect(document.querySelector('pre')!.textContent).toBe('{"id":"abc123",')
    // No status badge is invented for a payload that could not be read.
    expect(screen.queryByText('posted')).not.toBeInTheDocument()
    expect(screen.queryByText('pending')).not.toBeInTheDocument()
  })

  it('reports loading, not an empty archive, while the fetch is still outstanding', () => {
    vi.mocked(api.get).mockReturnValue(new Promise(() => {}))
    renderTab(5)

    expect(screen.getByText('Loading raw history...')).toBeInTheDocument()
    expect(screen.queryByText('No archived payloads for this transaction.')).not.toBeInTheDocument()
  })

  it('shows the manual empty state for a hand-entered transaction', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [], error: null })
    renderTab(5, true)
    await screen.findByText('No provider data — this transaction was entered manually.')
  })

  it('shows the empty-archive state for a synced transaction with no payloads', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [], error: null })
    renderTab(6, false)
    await screen.findByText('No archived payloads for this transaction.')
  })

  it('renders an endpoint error inline without firing a toast', async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: null,
      error: { message: 'The sync archive is unavailable.', code: 'ARCHIVE_UNAVAILABLE' },
    })
    renderTab()

    await screen.findByText('The sync archive is unavailable.')
    expect(vi.mocked(toast.error)).not.toHaveBeenCalled()
  })

  it('does not re-fetch when the tab is re-opened for the same transaction', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [makeEntry()], error: null })
    const { unmount } = renderTab(5)
    await screen.findByText('1 sighting from SimpleFIN')
    unmount()

    renderTab(5)
    await screen.findByText('1 sighting from SimpleFIN')
    expect(vi.mocked(api.get)).toHaveBeenCalledTimes(1)
  })
})
