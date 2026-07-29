import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { DndContext } from '@dnd-kit/core'
import { Provider } from 'react-redux'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { store } from '../../app/store'
import { api } from '../../api/client'
import { fetchNewTransactions, setActiveTab } from './transactionSlice'
import type { TransactionResponse } from '../../types/transaction'
import TransactionPane from './TransactionPane'

// Rows only render for endpoints seeded into this map; every other tab fetch
// resolves empty so the pane settles immediately.
const { getResponses } = vi.hoisted(() => ({
  getResponses: new Map<string, unknown>(),
}))

vi.mock('../../api/client', () => ({
  api: {
    get: vi.fn((endpoint: string) =>
      Promise.resolve({ data: getResponses.get(endpoint) ?? [], error: null }),
    ),
    post: vi.fn(() => Promise.resolve({ data: null, error: null })),
    put: vi.fn(() => Promise.resolve({ data: null, error: null })),
    delete: vi.fn(() => Promise.resolve({ data: true, error: null })),
  },
}))

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn() },
}))

beforeEach(() => {
  getResponses.clear()
  vi.clearAllMocks()
  // The app store is a singleton shared by every test in this file. Without an explicit
  // reset, rows fetched by the previous test are still in state when the next one mounts,
  // and a tab's assertions could pass on those leftovers rather than on the rows it seeded.
  store.dispatch(fetchNewTransactions.fulfilled([], ''))
  store.dispatch(setActiveTab('new'))
})

function makeTransaction(overrides: Partial<TransactionResponse> = {}): TransactionResponse {
  return {
    id: 5,
    accountId: null,
    accountName: null,
    date: '2026-07-10',
    description: 'Coffee',
    payee: '',
    memo: '',
    amount: -12.5,
    notes: null,
    isDeleted: false,
    isPending: false,
    isManual: true,
    assignments: [],
    ...overrides,
  }
}

function renderPane() {
  render(
    <Provider store={store}>
      <DndContext>
        <TransactionPane />
      </DndContext>
    </Provider>,
  )
  return screen.getByPlaceholderText('Search transactions...')
}

describe('TransactionPane search box', () => {
  it('keeps the typed query while the tab is unchanged', () => {
    const search = renderPane()
    fireEvent.change(search, { target: { value: 'coffee' } })
    expect(search).toHaveValue('coffee')
  })

  it('clears the query when the active tab changes', async () => {
    const search = renderPane()
    fireEvent.change(search, { target: { value: 'coffee' } })
    expect(search).toHaveValue('coffee')

    fireEvent.click(screen.getByRole('button', { name: /Tracked/ }))

    await waitFor(() => expect(search).toHaveValue(''))
  })
})

describe('TransactionPane row actions', () => {
  it('renders no delete control on a New-tab row', async () => {
    getResponses.set('/api/transactions/new', [makeTransaction()])
    renderPane()

    await screen.findByText('Coffee')

    expect(screen.queryByTitle('Delete')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull()
  })

  // With the row-level trash icon gone, the edit dialog is the only way to delete a New
  // transaction. This covers that path end to end so it can't regress silently.
  it('deletes a New-tab transaction through the edit dialog', async () => {
    getResponses.set('/api/transactions/new', [makeTransaction()])
    renderPane()

    fireEvent.click(await screen.findByText('Coffee'))
    fireEvent.click(await screen.findByRole('button', { name: 'Delete' }))

    await waitFor(() =>
      expect(vi.mocked(api.delete)).toHaveBeenCalledWith('/api/transactions/5'),
    )
  })

  it('keeps Restore and Delete on a Deleted-tab row', async () => {
    getResponses.set('/api/transactions/deleted', [
      makeTransaction({ isDeleted: true, isManual: true }),
    ])
    renderPane()

    fireEvent.click(screen.getByRole('button', { name: 'Deleted' }))

    expect(await screen.findByRole('button', { name: 'Restore' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete' })).toBeInTheDocument()
  })
})
