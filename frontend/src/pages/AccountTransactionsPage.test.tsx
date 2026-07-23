import { render, screen, waitFor, within } from '@testing-library/react'
import { Provider } from 'react-redux'
import { configureStore } from '@reduxjs/toolkit'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import accountTransactionsReducer from '../features/accountTransactions/accountTransactionsSlice'
import type { AccountTransactions } from '../types/accountTransactions'
import type { TransactionResponse } from '../types/transaction'
import AccountTransactionsPage from './AccountTransactionsPage'

const getMock = vi.fn()
vi.mock('../api/client', () => ({
  api: {
    get: (endpoint: string) => getMock(endpoint),
  },
}))

function tx(overrides: Partial<TransactionResponse>): TransactionResponse {
  return {
    id: 1,
    accountId: 3,
    accountName: 'Keybank Checking',
    date: '2026-07-20T00:00:00Z',
    description: 'KROGER',
    payee: '',
    memo: '',
    amount: -52.1,
    notes: null,
    isDeleted: false,
    isPending: false,
    isManual: false,
    assignments: [],
    ...overrides,
  }
}

function renderPage(accountId = '3') {
  const store = configureStore({ reducer: { accountTransactions: accountTransactionsReducer } })
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/accounts/${accountId}`]}>
        <Routes>
          <Route path="/accounts/:accountId" element={<AccountTransactionsPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  )
}

describe('AccountTransactionsPage', () => {
  beforeEach(() => {
    getMock.mockReset()
  })

  it('requests transactions for the account in the route', async () => {
    getMock.mockResolvedValue({
      data: { accountId: 3, accountName: 'Keybank Checking', transactions: [] } satisfies AccountTransactions,
      error: null,
    })
    renderPage('3')
    await waitFor(() => expect(getMock).toHaveBeenCalledWith('/api/transactions/by-account/3'))
  })

  it('renders the account name and a grid of its transactions', async () => {
    const data: AccountTransactions = {
      accountId: 3,
      accountName: 'Keybank Checking',
      transactions: [
        tx({ id: 1, description: 'KROGER', amount: -52.1, assignments: [{ id: 10, lineItemId: 5, lineItemName: 'Groceries', amount: -52.1 }] }),
        tx({ id: 2, description: 'PAYCHECK', amount: 2000, assignments: [] }),
      ],
    }
    getMock.mockResolvedValue({ data, error: null })

    renderPage()

    expect(await screen.findByRole('heading', { name: 'Keybank Checking' })).toBeInTheDocument()

    // Assigned transaction shows the budget line item name.
    expect(screen.getByText('Groceries')).toBeInTheDocument()

    // Unassigned transaction shows a dash in the budget item column.
    const paycheckRow = screen.getByText('PAYCHECK').closest('tr')!
    expect(within(paycheckRow).getByText('—')).toBeInTheDocument()
    expect(within(paycheckRow).getByText('+$2,000.00')).toBeInTheDocument()
  })

  it('marks soft-deleted transactions', async () => {
    getMock.mockResolvedValue({
      data: {
        accountId: 3,
        accountName: 'Keybank Checking',
        transactions: [tx({ id: 9, description: 'OLD CHARGE', isDeleted: true })],
      } satisfies AccountTransactions,
      error: null,
    })

    renderPage()

    const row = (await screen.findByText('OLD CHARGE')).closest('tr')!
    expect(within(row).getByText('deleted')).toBeInTheDocument()
    expect(row.className).toContain('line-through')
  })

  it('shows an empty state when the account has no transactions', async () => {
    getMock.mockResolvedValue({
      data: { accountId: 3, accountName: 'Keybank Checking', transactions: [] } satisfies AccountTransactions,
      error: null,
    })
    renderPage()
    expect(await screen.findByText('No transactions for this account.')).toBeInTheDocument()
  })

  it('shows the error message when the request fails', async () => {
    getMock.mockResolvedValue({ data: null, error: { message: 'Account not found.', code: 'ACCOUNT_NOT_FOUND' } })
    renderPage()
    await waitFor(() => expect(screen.getByText('Account not found.')).toBeInTheDocument())
  })
})
