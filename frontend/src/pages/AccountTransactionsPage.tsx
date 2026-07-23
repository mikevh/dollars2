import { useEffect, useState, type ChangeEvent } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import {
  faArrowLeft,
  faSort,
  faSortUp,
  faSortDown,
} from '@fortawesome/free-solid-svg-icons'
import {
  flexRender,
  getCoreRowModel,
  useReactTable,
  type ColumnDef,
  type OnChangeFn,
  type PaginationState,
  type SortingState,
} from '@tanstack/react-table'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import {
  fetchAccountTransactions,
  clearAccountTransactions,
} from '../features/accountTransactions/accountTransactionsSlice'
import { formatCurrency } from '../utils/format'
import type { TransactionResponse } from '../types/transaction'

const PAGE_SIZE_OPTIONS = [10, 50, 100, 500]
const DEFAULT_PAGE_SIZE = 100
const PAGE_SIZE_STORAGE_KEY = 'accountTxPageSize'
const SEARCH_DEBOUNCE_MS = 300

// A single global page-size preference shared across all account pages. Falls
// back to the default when localStorage is empty or holds an unsupported value.
function loadPageSize(): number {
  const stored = Number(localStorage.getItem(PAGE_SIZE_STORAGE_KEY))
  return PAGE_SIZE_OPTIONS.includes(stored) ? stored : DEFAULT_PAGE_SIZE
}

function formatDate(date: string): string {
  return new Date(date.slice(0, 10) + 'T00:00:00').toLocaleDateString()
}

function Amount({ transaction }: { transaction: TransactionResponse }) {
  const isIncome = transaction.amount > 0
  return (
    <span className={`tabular-nums ${isIncome ? 'font-bold' : 'font-medium'}`}>
      {isIncome ? '+' : '-'}
      {formatCurrency(Math.abs(transaction.amount))}
    </span>
  )
}

function BudgetItem({ transaction }: { transaction: TransactionResponse }) {
  if (transaction.assignments.length === 0) {
    return <span className="text-muted">—</span>
  }
  return <span>{transaction.assignments.map((a) => a.lineItemName).join(', ')}</span>
}

// Accessors exist only so TanStack marks the columns sortable; actual sorting
// happens server-side (manualSorting), so the accessed values are never used to order rows.
const columns: ColumnDef<TransactionResponse>[] = [
  { id: 'date', header: 'Date', accessorKey: 'date' },
  { id: 'description', header: 'Description', accessorFn: (t) => t.payee || t.description },
  { id: 'amount', header: 'Amount', accessorKey: 'amount' },
  { id: 'budgetItem', header: 'Budget item', enableSorting: false },
]

export default function AccountTransactionsPage() {
  const dispatch = useAppDispatch()
  const { accountId } = useParams<{ accountId: string }>()
  const { data, loading, error } = useAppSelector((state) => state.accountTransactions)

  const [sorting, setSorting] = useState<SortingState>([{ id: 'date', desc: true }])
  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: loadPageSize(),
  })
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')

  // Debounce the search box; a new term always returns to the first page.
  useEffect(() => {
    const handle = setTimeout(() => {
      setDebouncedSearch(search)
      setPagination((prev) => (prev.pageIndex === 0 ? prev : { ...prev, pageIndex: 0 }))
    }, SEARCH_DEBOUNCE_MS)
    return () => {
      clearTimeout(handle)
    }
  }, [search])

  const sort = sorting[0]?.id ?? 'date'
  const dir = sorting[0]?.desc === false ? 'asc' : 'desc'

  useEffect(() => {
    const id = Number(accountId)
    if (!Number.isNaN(id)) {
      dispatch(
        fetchAccountTransactions({
          accountId: id,
          page: pagination.pageIndex + 1,
          size: pagination.pageSize,
          sort,
          dir,
          q: debouncedSearch,
        })
      )
    }
  }, [dispatch, accountId, pagination.pageIndex, pagination.pageSize, sort, dir, debouncedSearch])

  useEffect(() => {
    return () => {
      dispatch(clearAccountTransactions())
    }
  }, [dispatch])

  const transactions = data?.transactions ?? []
  const totalCount = data?.totalCount ?? 0
  const pageCount = Math.max(1, Math.ceil(totalCount / pagination.pageSize))

  // Changing the sort returns to the first page.
  const handleSortingChange: OnChangeFn<SortingState> = (updater) => {
    setSorting(updater)
    setPagination((prev) => ({ ...prev, pageIndex: 0 }))
  }

  // Persist the chosen size globally and return to the first page.
  const handlePageSizeChange = (e: ChangeEvent<HTMLSelectElement>) => {
    const size = Number(e.target.value)
    localStorage.setItem(PAGE_SIZE_STORAGE_KEY, String(size))
    setPagination({ pageIndex: 0, pageSize: size })
  }

  const table = useReactTable({
    data: transactions,
    columns,
    state: { sorting, pagination },
    manualPagination: true,
    manualSorting: true,
    manualFiltering: true,
    enableSortingRemoval: false,
    enableMultiSort: false,
    pageCount,
    onSortingChange: handleSortingChange,
    onPaginationChange: setPagination,
    getCoreRowModel: getCoreRowModel(),
  })

  const rangeStart = totalCount === 0 ? 0 : pagination.pageIndex * pagination.pageSize + 1
  const rangeEnd = Math.min((pagination.pageIndex + 1) * pagination.pageSize, totalCount)

  return (
    <div className="flex min-h-screen flex-col bg-bg pb-14 text-text">
      <div className="relative flex items-center border-b-2 border-divider px-4 py-3">
        <Link to="/accounts" className="btn btn-ghost text-[13px]" title="Back to accounts">
          <FontAwesomeIcon icon={faArrowLeft} className="h-[13px] w-[13px]" />
          <span>Accounts</span>
        </Link>
        <h2 className="absolute left-1/2 -translate-x-1/2 text-[18px]">
          {data?.accountName ?? 'Account'}
        </h2>
      </div>

      <div className="mx-auto w-full max-w-[860px] px-4 py-6">
        {loading && !data && <div className="text-muted py-12 text-center">Loading...</div>}

        {!data && error && <div className="py-12 text-center text-accent">{error}</div>}

        {data && (
          <>
            <div className="mb-3">
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search description or amount..."
                className="input"
                aria-label="Search transactions"
              />
            </div>

            {transactions.length === 0 ? (
              <div className="text-muted py-12 text-center">
                {debouncedSearch
                  ? 'No transactions match your search.'
                  : 'No transactions for this account.'}
              </div>
            ) : (
              <div className="overflow-x-auto border border-divider bg-surface shadow-elev-sm">
                <table
                  className={`w-full text-[14px] ${loading ? 'opacity-60' : ''}`}
                  aria-busy={loading}
                >
                  <thead>
                    {table.getHeaderGroups().map((headerGroup) => (
                      <tr
                        key={headerGroup.id}
                        className="text-muted border-b-2 border-divider text-left text-[12px] font-semibold uppercase tracking-wide"
                      >
                        {headerGroup.headers.map((header) => {
                          const canSort = header.column.getCanSort()
                          const sorted = header.column.getIsSorted()
                          const isAmount = header.column.id === 'amount'
                          const label = flexRender(
                            header.column.columnDef.header,
                            header.getContext()
                          )
                          return (
                            <th
                              key={header.id}
                              className={`px-4 py-2 font-semibold ${isAmount ? 'text-right' : ''}`}
                            >
                              {canSort ? (
                                <button
                                  type="button"
                                  onClick={header.column.getToggleSortingHandler()}
                                  className={`inline-flex items-center gap-1 uppercase tracking-wide hover:text-text ${isAmount ? 'flex-row-reverse' : ''}`}
                                >
                                  <span>{label}</span>
                                  <FontAwesomeIcon
                                    icon={
                                      sorted === 'asc'
                                        ? faSortUp
                                        : sorted === 'desc'
                                          ? faSortDown
                                          : faSort
                                    }
                                    className={`h-[11px] w-[11px] ${sorted ? '' : 'opacity-40'}`}
                                  />
                                </button>
                              ) : (
                                label
                              )}
                            </th>
                          )
                        })}
                      </tr>
                    ))}
                  </thead>
                  <tbody>
                    {table.getRowModel().rows.map((row) => {
                      const t = row.original
                      return (
                        <tr
                          key={t.id}
                          className={`border-b border-divider last:border-b-0 ${t.isDeleted ? 'text-muted line-through' : ''}`}
                        >
                          <td className="whitespace-nowrap px-4 py-2 tabular-nums">
                            {formatDate(t.date)}
                          </td>
                          <td className="px-4 py-2">
                            <span className="align-middle">{t.payee || t.description}</span>
                            {t.isManual && (
                              <span className="text-muted ml-2 border border-divider px-1 text-[11px] uppercase tracking-wide no-underline">
                                manual
                              </span>
                            )}
                            {t.isPending && (
                              <span className="text-muted ml-2 border border-divider px-1 text-[11px] uppercase tracking-wide no-underline">
                                pending
                              </span>
                            )}
                            {t.isDeleted && (
                              <span className="text-accent ml-2 border border-divider px-1 text-[11px] uppercase tracking-wide no-underline">
                                deleted
                              </span>
                            )}
                          </td>
                          <td className="whitespace-nowrap px-4 py-2 text-right">
                            <Amount transaction={t} />
                          </td>
                          <td className="px-4 py-2">
                            <BudgetItem transaction={t} />
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            )}

            {transactions.length > 0 && (
              <div className="text-muted mt-3 flex items-center justify-between text-[13px]">
                <div className="flex items-center gap-3">
                  <span className="tabular-nums">
                    {rangeStart}–{rangeEnd} of {totalCount}
                  </span>
                  <select
                    value={pagination.pageSize}
                    onChange={handlePageSizeChange}
                    className="input w-auto text-[13px]"
                    aria-label="Transactions per page"
                  >
                    {PAGE_SIZE_OPTIONS.map((size) => (
                      <option key={size} value={size}>
                        {size} / page
                      </option>
                    ))}
                  </select>
                </div>
                <div className="flex items-center gap-3">
                  <button
                    type="button"
                    className="btn btn-ghost text-[13px]"
                    onClick={() => table.previousPage()}
                    disabled={!table.getCanPreviousPage()}
                  >
                    Prev
                  </button>
                  <span className="tabular-nums">
                    Page {pagination.pageIndex + 1} of {pageCount}
                  </span>
                  <button
                    type="button"
                    className="btn btn-ghost text-[13px]"
                    onClick={() => table.nextPage()}
                    disabled={!table.getCanNextPage()}
                  >
                    Next
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  )
}
