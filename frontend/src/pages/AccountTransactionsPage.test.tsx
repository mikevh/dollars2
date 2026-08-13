import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { Provider } from 'react-redux'
import { configureStore } from '@reduxjs/toolkit'
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import accountTransactionsReducer from '../features/accountTransactions/accountTransactionsSlice'
import type { AccountTransactions } from '../types/accountTransactions'
import type { TransactionResponse } from '../types/transaction'
import AccountTransactionsPage from './AccountTransactionsPage'

const SEARCH_DEBOUNCE_MS = 300

const getMock = vi.fn()
vi.mock('../api/client', () => ({
  api: { get: (endpoint: string) => getMock(endpoint) },
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
  totalCount = transactions.length,
  sourceType = 'SimpleFIN'
): AccountTransactions {
  return { accountId: 3, accountName: 'Keybank Checking', sourceType, transactions, totalCount }
}

function renderPage(accountId = '3') {
  const store = configureStore({
    reducer: { accountTransactions: accountTransactionsReducer },
  })
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

// Same route, two account ids — the case React Router serves by reusing the component instance.
function renderPageWithSwitcher(accountId: string, nextAccountId: string) {
  const store = configureStore({
    reducer: { accountTransactions: accountTransactionsReducer },
  })
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[`/accounts/${accountId}`]}>
        <Link to={`/accounts/${nextAccountId}`}>Switch account</Link>
        <Routes>
          <Route path="/accounts/:accountId" element={<AccountTransactionsPage />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  )
}

function switchAccount() {
  fireEvent.click(screen.getByRole('link', { name: 'Switch account' }))
}

function lastUrl(): string {
  return getMock.mock.calls.at(-1)![0] as string
}

function urlsSince(callCount: number): string[] {
  return getMock.mock.calls.slice(callCount).map((call) => call[0] as string)
}

function dirOf(url: string): string | null {
  return new URLSearchParams(url.split('?')[1] ?? '').get('dir')
}

function sizeOf(url: string): string | null {
  return new URLSearchParams(url.split('?')[1] ?? '').get('size')
}

function includeDeletedOf(url: string): string | null {
  return new URLSearchParams(url.split('?')[1] ?? '').get('includeDeleted')
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

  it('hides deleted transactions by default', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(lastUrl()).toContain('page=1'))

    expect(includeDeletedOf(lastUrl())).toBeNull()
    expect((screen.getByLabelText('Show deleted') as HTMLInputElement).checked).toBe(false)
  })

  it('re-requests with deleted rows, resets to page 1, and persists the choice', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(lastUrl()).toContain('page=1'))

    // Move off page 1 so the reset is observable.
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(lastUrl()).toContain('page=2'))

    fireEvent.click(screen.getByLabelText('Show deleted'))
    await waitFor(() => {
      expect(includeDeletedOf(lastUrl())).toBe('true')
      expect(lastUrl()).toContain('page=1')
    })
    expect(localStorage.getItem('accountTxShowDeleted')).toBe('true')
  })

  it('restores the persisted show-deleted choice on load', async () => {
    localStorage.setItem('accountTxShowDeleted', 'true')
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(includeDeletedOf(lastUrl())).toBe('true'))
    expect((screen.getByLabelText('Show deleted') as HTMLInputElement).checked).toBe(true)
  })

  it('unchecking stops requesting deleted rows and persists the choice', async () => {
    localStorage.setItem('accountTxShowDeleted', 'true')
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPage()
    await waitFor(() => expect(includeDeletedOf(lastUrl())).toBe('true'))

    fireEvent.click(screen.getByLabelText('Show deleted'))
    await waitFor(() => expect(includeDeletedOf(lastUrl())).toBeNull())
    expect(localStorage.getItem('accountTxShowDeleted')).toBe('false')
  })

  it('keeps the show-deleted toggle reachable when the page comes back empty', async () => {
    // An account whose rows are all deleted renders the empty state; the toggle is the
    // only way back to them, so it must survive an empty page.
    getMock.mockResolvedValue({ data: page([]), error: null })
    renderPage()

    expect(await screen.findByText('No transactions for this account.')).toBeInTheDocument()
    expect(screen.getByLabelText('Show deleted')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next' })).toBeNull()
  })

  it('says deleted rows are hidden on an empty page, so the message is not misleading', async () => {
    // An account whose rows are all soft-deleted looks identical to an empty one.
    getMock.mockResolvedValue({ data: page([]), error: null })
    renderPage()

    expect(await screen.findByText('Deleted transactions are hidden.')).toBeInTheDocument()
  })

  it('drops the hidden-deleted hint once deleted rows are being shown', async () => {
    localStorage.setItem('accountTxShowDeleted', 'true')
    getMock.mockResolvedValue({ data: page([]), error: null })
    renderPage()

    expect(await screen.findByText('No transactions for this account.')).toBeInTheDocument()
    expect(screen.queryByText('Deleted transactions are hidden.')).toBeNull()
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

  it('starts another account at page 1 with the default sort and no search', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPageWithSwitcher('3', '7')
    await waitFor(() => expect(lastUrl()).toContain('/by-account/3'))

    // Dirty every piece of per-account state before navigating away.
    fireEvent.click(screen.getByRole('button', { name: /description/i }))
    await waitFor(() => expect(lastUrl()).toContain('sort=description'))

    fireEvent.change(screen.getByLabelText('Search transactions'), {
      target: { value: 'coffee' },
    })
    await waitFor(() => expect(lastUrl()).toContain('q=coffee'))

    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(lastUrl()).toContain('page=2'))

    const callsBefore = getMock.mock.calls.length
    switchAccount()
    await waitFor(() => expect(lastUrl()).toContain('/by-account/7'))

    // Not just the last request — no request for the new account may carry the old
    // account's page, sort or search, or the user sees the wrong rows on the way there.
    const newAccountUrls = urlsSince(callsBefore)
    expect(newAccountUrls.length).toBeGreaterThan(0)
    for (const url of newAccountUrls) {
      expect(url).toBe('/api/transactions/by-account/7?page=1&size=100&sort=date&dir=desc')
    }
    expect((screen.getByLabelText('Search transactions') as HTMLInputElement).value).toBe('')
  })

  it('shows a loading state, not the previous account, while another account loads', async () => {
    getMock.mockResolvedValue({ data: page([tx({ description: 'KROGER' })]), error: null })
    renderPageWithSwitcher('3', '7')
    expect(await screen.findByRole('heading', { name: 'Keybank Checking' })).toBeInTheDocument()

    // Hold the next account's response open so the in-flight state is observable.
    let resolveNext!: (value: { data: AccountTransactions; error: null }) => void
    getMock.mockImplementationOnce(
      () => new Promise<{ data: AccountTransactions; error: null }>((resolve) => {
        resolveNext = resolve
      }),
    )

    switchAccount()

    expect(await screen.findByText('Loading...')).toBeInTheDocument()
    expect(screen.queryByText('KROGER')).not.toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Account' })).toBeInTheDocument()

    resolveNext({
      data: { accountId: 7, accountName: 'Chase Savings', transactions: [tx({ id: 2, description: 'SHELL' })], totalCount: 1 },
      error: null,
    })
    expect(await screen.findByRole('heading', { name: 'Chase Savings' })).toBeInTheDocument()
    expect(screen.getByText('SHELL')).toBeInTheDocument()
  })

  it('does not let a fresh mount snap a newly paged view back to page 1', async () => {
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPageWithSwitcher('3', '7')
    await waitFor(() => expect(lastUrl()).toContain('/by-account/3'))

    switchAccount()
    // Page off 1 inside the debounce window — a mount-scheduled timer would fire afterwards
    // and reset pageIndex for a search term that never changed.
    await screen.findByRole('button', { name: 'Next' })
    fireEvent.click(screen.getByRole('button', { name: 'Next' }))
    await waitFor(() => expect(lastUrl()).toContain('page=2'))

    await new Promise((resolve) => setTimeout(resolve, SEARCH_DEBOUNCE_MS * 2))
    expect(lastUrl()).toContain('page=2')
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument()
  })

  it('keeps the persisted page size when switching accounts', async () => {
    localStorage.setItem('accountTxPageSize', '50')
    getMock.mockResolvedValue({ data: page([tx({})], 250), error: null })
    renderPageWithSwitcher('3', '7')
    await waitFor(() => expect(sizeOf(lastUrl())).toBe('50'))

    switchAccount()
    await waitFor(() => expect(lastUrl()).toContain('/by-account/7'))

    expect(sizeOf(lastUrl())).toBe('50')
    expect((screen.getByLabelText('Transactions per page') as HTMLSelectElement).value).toBe('50')
  })
})
