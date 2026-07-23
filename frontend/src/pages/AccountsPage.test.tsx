import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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
    post: (endpoint: string) => postMock(endpoint),
  },
}))
vi.mock('react-hot-toast', () => ({
  default: { success: vi.fn(), error: vi.fn() },
}))

function renderPage() {
  const store = configureStore({ reducer: { accounts: accountsReducer } })
  render(
    <Provider store={store}>
      <MemoryRouter>
        <AccountsPage />
      </MemoryRouter>
    </Provider>,
  )
}

describe('AccountsPage', () => {
  beforeEach(() => {
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

    await waitFor(() => expect(postMock).toHaveBeenCalledWith('/api/sync/connection/abc123'))
    // Accounts are refetched after a successful sync.
    await waitFor(() => expect(getMock).toHaveBeenCalledWith('/api/accounts'))
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
