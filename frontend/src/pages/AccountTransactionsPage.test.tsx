import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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

function page(
  transactions: TransactionResponse[],
  totalCount = transactions.length
): AccountTransactions {
  return { accountId: 3, accountName: 'Keybank Checking', transactions, totalCount }
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

function lastUrl(): string {
  return getMock.mock.calls.at(-1)![0] as string
}

function dirOf(url: string): string | null {
  return new URLSearchParams(url.split('?')[1] ?? '').get('dir')
}

function sizeOf(url: string): string | null {
  return new URLSearchParams(url.split('?')[1] ?? '').get('size')
}

describe('AccountTransactionsPage', () => {
  beforeEach(() => {
    getMock.mockReset()
    localStorage.clear()
  })

  it('requests the first page (size 100, date desc) for the account in the route', async () => {
    getMock.mockResolvedValue({ data: page([]), error: null })
    renderPage('3')
    await waitFor(() =>
      expect(getMock).toHaveBeenCalledWith(
        '/api/transactions/by-account/3?page=1&size=100&sort=date&dir=desc',
      ),
    )
  })

  it('renders the account name and a grid of its transactions', async () => {
    getMock.mockResolvedValue({
      data: page([
        tx({ id: 1, description: 'KROGER', amount: -52.1, assignments: [{ id: 10, lineItemId: 5, lineItemName: 'Groceries', amount: -52.1 }] }),
        tx({ id: 2, description: 'PAYCHECK', amount: 2000, assignments: [] }),
      ]),
      error: null,
    })

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
      data: page([tx({ id: 9, description: 'OLD CHARGE', isDeleted: true })]),
      error: null,
    })

    renderPage()

    const row = (await screen.findByText('OLD CHARGE')).closest('tr')!
    expect(within(row).getByText('deleted')).toBeInTheDocument()
    expect(row.className).toContain('line-through')
  })

  it('toggles sort when a sortable header is clicked and re-requests', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(lastUrl()).toContain('sort=date&dir=desc'))

    fireEvent.click(screen.getByRole('button', { name: /description/i }))
    await waitFor(() => expect(lastUrl()).toContain('sort=description'))
    const firstDir = dirOf(lastUrl())

    fireEvent.click(screen.getByRole('button', { name: /description/i }))
    await waitFor(() => expect(dirOf(lastUrl())).not.toBe(firstDir))
    expect(lastUrl()).toContain('sort=description')
  })

  it('does not make the budget item column sortable', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await screen.findByText('Budget item')
    expect(screen.getByText('Budget item').closest('button')).toBeNull()
  })

  it('searches with a debounce and resets to the first page', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(lastUrl()).toContain('page=1'))

    // Advance off page 1 first so the reset is observable.
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(lastUrl()).toContain('page=2'))

    fireEvent.change(screen.getByLabelText('Search transactions'), {
      target: { value: 'coffee' },
    })
    await waitFor(() => {
      expect(lastUrl()).toContain('q=coffee')
      expect(lastUrl()).toContain('page=1')
    })
  })

  it('pages with prev/next and reflects the total count', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(lastUrl()).toContain('page=1'))

    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument()
    expect(screen.getByText('1–100 of 250')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Prev' })).toBeDisabled()

    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(lastUrl()).toContain('page=2'))

    fireEvent.click(screen.getByRole('button', { name: 'Prev' }))
    await waitFor(() => expect(lastUrl()).toContain('page=1'))
  })

  it('offers page sizes 10 / 50 / 100 / 500 and defaults to 100', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    const select = (await screen.findByLabelText('Transactions per page')) as HTMLSelectElement
    expect(select.value).toBe('100')
    expect(
      Array.from(select.options).map((o) => o.value),
    ).toEqual(['10', '50', '100', '500'])
  })

  it('re-fetches the chosen size, resets to page 1, and persists it', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(lastUrl()).toContain('page=1'))

    // Move off page 1 so the reset is observable.
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(lastUrl()).toContain('page=2'))

    fireEvent.change(screen.getByLabelText('Transactions per page'), {
      target: { value: '10' },
    })
    await waitFor(() => {
      expect(sizeOf(lastUrl())).toBe('10')
      expect(lastUrl()).toContain('page=1')
    })
    expect(localStorage.getItem('accountTxPageSize')).toBe('10')
    expect(screen.getByText('Page 1 of 25')).toBeInTheDocument()
    expect(screen.getByText('1–10 of 250')).toBeInTheDocument()
  })

  it('restores the persisted page size on load', async () => {
    localStorage.setItem('accountTxPageSize', '50')
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(sizeOf(lastUrl())).toBe('50'))
    expect((screen.getByLabelText('Transactions per page') as HTMLSelectElement).value).toBe('50')
  })

  it('falls back to 100 when the stored page size is garbage', async () => {
    localStorage.setItem('accountTxPageSize', 'not-a-number')
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(sizeOf(lastUrl())).toBe('100'))
  })

  it('shows an empty state when the account has no transactions', async () => {
    getMock.mockResolvedValue({ data: page([]), error: null })
    renderPage()
    expect(await screen.findByText('No transactions for this account.')).toBeInTheDocument()
  })

  it('shows the error message when the request fails', async () => {
    getMock.mockResolvedValue({ data: null, error: { message: 'Account not found.', code: 'ACCOUNT_NOT_FOUND' } })
    renderPage()
    await waitFor(() => expect(screen.getByText('Account not found.')).toBeInTheDocument())
  })
})
