import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { Provider } from 'react-redux'
import { configureStore } from '@reduxjs/toolkit'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import accountsReducer from '../features/accounts/accountsSlice'
import type { AccountGroup } from '../types/account'
import AccountsPage from './AccountsPage'

const getMock = vi.fn()
const postMock = vi.fn()
vi.mock('../api/client', () => ({
  api: {
    get: (endpoint: string) => getMock(endpoint),
    post: (endpoint: string, body?: unknown) => postMock(endpoint, body),
  },
}))
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}))

function renderPage() {
  const store = configureStore({ reducer: { accounts: accountsReducer } })
  return render(
    <Provider store={store}>
      <MemoryRouter>
        <AccountsPage />
      </MemoryRouter>
    </Provider>,
  )
}

describe('AccountsPage', () => {
  beforeEach(() => {
    // A test that installs fake timers must not leak them into the rest of the file: the
    // real-timer polling in findBy*/waitFor would hang everywhere downstream.
    vi.useRealTimers()
    getMock.mockReset()
    postMock.mockReset()
  })

  it('renders accounts grouped by connection with names and last-sync', async () => {
    const groups: AccountGroup[] = [
      {
        connectionId: 'abc123',
        sourceType: 'SimpleFIN',
        accounts: [
          { id: 1, name: 'Keybank Checking', lastSyncedAt: '2026-07-22T10:00:00Z', lastStatus: 'Success', balance: 1234.56 },
          { id: 2, name: 'Keybank Savings', lastSyncedAt: null, lastStatus: null, balance: null },
        ],
      },
      {
        connectionId: 'manual',
        sourceType: 'Manual',
        accounts: [{ id: 3, name: 'Cash', lastSyncedAt: null, lastStatus: null, balance: null }],
      },
    ]
    getMock.mockResolvedValue({ data: groups, error: null })

    renderPage()

    expect(await screen.findByText('Keybank Checking')).toBeInTheDocument()
    expect(screen.getByText('Keybank Savings')).toBeInTheDocument()
    expect(screen.getByText('SimpleFIN')).toBeInTheDocument()

    // Manual accounts group present.
    const manualHeader = screen.getByText('Manual accounts')
    expect(manualHeader).toBeInTheDocument()
    expect(screen.getByText('Cash')).toBeInTheDocument()

    // The account with a stored balance shows it formatted as currency; balance-less accounts show none.
    expect(screen.getByText('$1,234.56')).toBeInTheDocument()

    // Never-synced accounts show a dash. Two accounts never synced (Savings + Cash).
    expect(screen.getAllByText('—')).toHaveLength(2)
  })

  it('links each account to its transactions page', async () => {
    getMock.mockResolvedValue({
      data: [
        {
          connectionId: 'abc123',
          sourceType: 'SimpleFIN',
          accounts: [{ id: 7, name: 'Keybank Checking', lastSyncedAt: null, lastStatus: null, balance: null }],
        },
      ] satisfies AccountGroup[],
      error: null,
    })

    renderPage()

    const link = (await screen.findByText('Keybank Checking')).closest('a')!
    expect(link).toHaveAttribute('href', '/accounts/7')
  })

  it('shows a failure indicator for the last sync status', async () => {
    getMock.mockResolvedValue({
      data: [
        {
          connectionId: 'abc123',
          sourceType: 'SimpleFIN',
          accounts: [{ id: 1, name: 'Broken', lastSyncedAt: '2026-07-22T10:00:00Z', lastStatus: 'Failure', balance: null }],
        },
      ] satisfies AccountGroup[],
      error: null,
    })

    renderPage()

    const row = (await screen.findByText('Broken')).closest('li')!
    expect(within(row).getByText(/sync failed/)).toBeInTheDocument()
  })

  it('ages the relative sync time on its own and stops ticking on unmount', async () => {
    vi.useFakeTimers({ now: new Date('2026-07-22T10:00:00Z') })
    try {
      getMock.mockResolvedValue({
        data: [
          {
            connectionId: 'abc123',
            sourceType: 'SimpleFIN',
            accounts: [
              { id: 1, name: 'Keybank Checking', lastSyncedAt: '2026-07-22T10:00:00Z', lastStatus: 'Success', balance: null },
            ],
          },
        ] satisfies AccountGroup[],
        error: null,
      })

      const { unmount } = renderPage()

      // findBy*/waitFor poll on real timers, which the fake clock freezes — flush the mocked
      // fetch by hand instead.
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0)
      })
      const row = screen.getByText('Keybank Checking').closest('li')!
      expect(within(row).getByText('synced just now')).toBeInTheDocument()

      // No user interaction — only the clock moves.
      await act(async () => {
        vi.advanceTimersByTime(90_000)
      })
      expect(within(row).getByText('synced 1m ago')).toBeInTheDocument()

      await act(async () => {
        vi.advanceTimersByTime(60_000)
      })
      expect(within(row).getByText('synced 2m ago')).toBeInTheDocument()

      // The absolute timestamp stays in the title throughout.
      expect(within(row).getByText('synced 2m ago')).toHaveAttribute(
        'title',
        new Date('2026-07-22T10:00:00Z').toLocaleString(),
      )

      unmount()
      expect(vi.getTimerCount()).toBe(0)
    } finally {
      vi.useRealTimers()
    }
  })

  it('shows an empty state when there are no accounts', async () => {
    getMock.mockResolvedValue({ data: [], error: null })
    renderPage()
    expect(await screen.findByText('No accounts.')).toBeInTheDocument()
  })

  it('shows the error message when the request fails', async () => {
    getMock.mockResolvedValue({ data: null, error: { message: 'Boom', code: 'SERVER_ERROR' } })
    renderPage()
    await waitFor(() => expect(screen.getByText('Boom')).toBeInTheDocument())
  })

  it('syncs only the clicked connection group and refreshes accounts', async () => {
    getMock.mockResolvedValue({
      data: [
        {
          connectionId: 'abc123',
          sourceType: 'SimpleFIN',
          accounts: [{ id: 1, name: 'Keybank Checking', lastSyncedAt: null, lastStatus: null, balance: null }],
        },
        {
          connectionId: 'manual',
          sourceType: 'Manual',
          accounts: [{ id: 3, name: 'Cash', lastSyncedAt: null, lastStatus: null, balance: null }],
        },
      ] satisfies AccountGroup[],
      error: null,
    })
    postMock.mockResolvedValue({
      data: [{ accountId: 1, accountName: 'Keybank Checking', status: 'Success', transactionCount: 2, errorMessage: null }],
      error: null,
    })

    renderPage()

    // Only the syncable group has a Sync button; the Manual group has none.
    const syncButton = await screen.findByRole('button', { name: 'Sync' })
    expect(screen.getAllByRole('button', { name: 'Sync' })).toHaveLength(1)

    getMock.mockClear()
    fireEvent.click(syncButton)

    await waitFor(() => expect(postMock).toHaveBeenCalledWith('/api/sync/connection/abc123', undefined))
    // Accounts are refetched after a successful sync.
    await waitFor(() => expect(getMock).toHaveBeenCalledWith('/api/accounts'))
  })

  const singleSimplefinGroup = {
    data: [
      {
        connectionId: 'abc123',
        sourceType: 'SimpleFIN',
        accounts: [{ id: 1, name: 'Keybank Checking', lastSyncedAt: null, lastStatus: null, balance: null }],
      },
    ] satisfies AccountGroup[],
    error: null,
  }

  it('opens the re-sync dialog with a default of 180 days', async () => {
    getMock.mockResolvedValue(singleSimplefinGroup)
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Re-sync' }))

    const input = screen.getByRole('spinbutton') as HTMLInputElement
    expect(input.value).toBe('180')
  })

  it('dismisses the re-sync dialog on Escape and returns focus to the trigger', async () => {
    getMock.mockResolvedValue(singleSimplefinGroup)
    renderPage()

    const trigger = await screen.findByRole('button', { name: 'Re-sync' })
    trigger.focus()
    fireEvent.click(trigger)
    expect(screen.getByRole('spinbutton')).toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })

    expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
  })

  it('disables the confirm button for out-of-range or blank day counts', async () => {
    getMock.mockResolvedValue(singleSimplefinGroup)
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Re-sync' }))
    const input = screen.getByRole('spinbutton')
    // The dialog's confirm button shares the 'Re-sync' name; it's the one inside the dialog.
    const confirm = screen.getAllByRole('button', { name: 'Re-sync' }).at(-1)!

    expect(confirm).toBeEnabled() // default 180 is valid

    fireEvent.change(input, { target: { value: '0' } })
    expect(confirm).toBeDisabled()

    fireEvent.change(input, { target: { value: '731' } })
    expect(confirm).toBeDisabled()

    fireEvent.change(input, { target: { value: '' } })
    expect(confirm).toBeDisabled()

    fireEvent.change(input, { target: { value: '365' } })
    expect(confirm).toBeEnabled()
  })

  it('resyncs the connection with the chosen day count and toasts the new count', async () => {
    const toast = (await import('react-hot-toast')).default
    getMock.mockResolvedValue(singleSimplefinGroup)
    postMock.mockResolvedValue({
      data: [{ accountId: 1, accountName: 'Keybank Checking', status: 'Success', transactionCount: 3, errorMessage: null }],
      error: null,
    })

    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Re-sync' }))
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '90' } })
    getMock.mockClear()
    fireEvent.click(screen.getAllByRole('button', { name: 'Re-sync' }).at(-1)!)

    await waitFor(() =>
      expect(postMock).toHaveBeenCalledWith('/api/sync/connection/abc123/resync', { days: 90 }),
    )
    // Accounts are refetched after a successful resync.
    await waitFor(() => expect(getMock).toHaveBeenCalledWith('/api/accounts'))
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Synced 3 new transactions'))
    // Dialog closes on success.
    await waitFor(() => expect(screen.queryByRole('spinbutton')).not.toBeInTheDocument())
  })

  it('shows an error toast when a group sync fails', async () => {
    const toast = (await import('react-hot-toast')).default
    getMock.mockResolvedValue({
      data: [
        {
          connectionId: 'abc123',
          sourceType: 'SimpleFIN',
          accounts: [{ id: 1, name: 'Keybank Checking', lastSyncedAt: null, lastStatus: null, balance: null }],
        },
      ] satisfies AccountGroup[],
      error: null,
    })
    postMock.mockResolvedValue({ data: null, error: { message: 'A sync is already in progress.', code: 'SYNC_IN_PROGRESS' } })

    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Sync' }))

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith('A sync is already in progress.'))
  })
})
