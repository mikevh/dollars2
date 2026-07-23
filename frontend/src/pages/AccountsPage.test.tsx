import { render, screen, waitFor, within } from '@testing-library/react'
import { Provider } from 'react-redux'
import { configureStore } from '@reduxjs/toolkit'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import accountsReducer from '../features/accounts/accountsSlice'
import type { AccountGroup } from '../types/account'
import AccountsPage from './AccountsPage'

const getMock = vi.fn()
vi.mock('../api/client', () => ({
  api: {
    get: (endpoint: string) => getMock(endpoint),
  },
}))

function renderPage() {
  const store = configureStore({ reducer: { accounts: accountsReducer } })
  render(
    <Provider store={store}>
      <AccountsPage />
    </Provider>,
  )
}

describe('AccountsPage', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('renders accounts grouped by connection with names and last-sync', async () => {
    const groups: AccountGroup[] = [
      {
        connectionId: 'abc123',
        sourceType: 'SimpleFIN',
        accounts: [
          { id: 1, name: 'Keybank Checking', lastSyncedAt: '2026-07-22T10:00:00Z', lastStatus: 'Success' },
          { id: 2, name: 'Keybank Savings', lastSyncedAt: null, lastStatus: null },
        ],
      },
      {
        connectionId: 'manual',
        sourceType: 'Manual',
        accounts: [{ id: 3, name: 'Cash', lastSyncedAt: null, lastStatus: null }],
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

    // Never-synced accounts show a dash. Two accounts never synced (Savings + Cash).
    expect(screen.getAllByText('—')).toHaveLength(2)
  })

  it('shows a failure indicator for the last sync status', async () => {
    getMock.mockResolvedValue({
      data: [
        {
          connectionId: 'abc123',
          sourceType: 'SimpleFIN',
          accounts: [{ id: 1, name: 'Broken', lastSyncedAt: '2026-07-22T10:00:00Z', lastStatus: 'Failure' }],
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
})
