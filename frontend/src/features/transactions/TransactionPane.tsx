import { useCallback, useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import {
  fetchNewTransactions,
  fetchTrackedTransactions,
  fetchDeletedTransactions,
  fetchPendingTransactions,
  fetchCounts,
  restoreTransaction,
  hardDeleteTransaction,
  setActiveTab,
} from './transactionSlice'
import TransactionRow from './TransactionRow'
import TransactionEditDialog from './TransactionEditDialog'
import type { TransactionResponse } from '../../types/transaction'

const tabs = [
  { key: 'new' as const, label: 'New', showCount: true },
  { key: 'tracked' as const, label: 'Tracked', showCount: false },
  { key: 'deleted' as const, label: 'Deleted', showCount: false },
  { key: 'pending' as const, label: 'Pending', showCount: true },
]

interface TransactionPaneProps {
  onBudgetMutate?: () => void
}

export default function TransactionPane({ onBudgetMutate }: TransactionPaneProps) {
  const dispatch = useAppDispatch()
  const { transactions, loading, error, activeTab, counts } = useAppSelector((state) => state.transactions)
  const { currentYear, currentMonth } = useAppSelector((state) => state.budget)
  const [editingTransaction, setEditingTransaction] = useState<TransactionResponse | null | 'create'>(null)
  const [search, setSearch] = useState('')

  // Clear the search box when the tab changes. Adjusting state during render
  // (rather than in an effect) is React's recommended way to reset state on an
  // input change and avoids a cascading re-render.
  const [searchedTab, setSearchedTab] = useState(activeTab)
  if (searchedTab !== activeTab) {
    setSearchedTab(activeTab)
    setSearch('')
  }

  const query = search.trim().toLowerCase()
  const filteredTransactions = query
    ? transactions.filter((t) => {
        const fields = [t.payee, t.description, t.memo, Math.abs(t.amount).toFixed(2)]
        return fields.some((field) => field?.toLowerCase().includes(query))
      })
    : transactions

  const fetchCurrentTab = useCallback(() => {
    dispatch(fetchCounts())
    if (activeTab === 'new') {
      dispatch(fetchNewTransactions())
    } else if (activeTab === 'tracked') {
      const fromDate = new Date()
      fromDate.setMonth(fromDate.getMonth() - 2)
      dispatch(fetchTrackedTransactions({ fromDate: fromDate.toISOString().split('T')[0] }))
    } else if (activeTab === 'deleted') {
      dispatch(fetchDeletedTransactions())
    } else if (activeTab === 'pending') {
      dispatch(fetchPendingTransactions())
    }
  }, [dispatch, activeTab])

  useEffect(() => {
    fetchCurrentTab()
    // currentYear/currentMonth aren't read by fetchCurrentTab, but navigating to a
    // different budget month has to refresh the counts and the tracked list.
  }, [fetchCurrentTab, currentYear, currentMonth])

  const handleRestore = async (id: number) => {
    const result = await dispatch(restoreTransaction({ id }))
    if (restoreTransaction.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  const handleHardDelete = async (id: number) => {
    const result = await dispatch(hardDeleteTransaction({ id }))
    if (hardDeleteTransaction.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  const handleDialogMutate = () => {
    fetchCurrentTab()
    onBudgetMutate?.()
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex border-b-2 border-divider">
        {tabs.map((tab) => {
          const count = tab.showCount ? counts[tab.key] : 0
          const isActive = activeTab === tab.key
          return (
            <button
              key={tab.key}
              onClick={() => dispatch(setActiveTab(tab.key))}
              className={`flex min-w-0 flex-1 items-center justify-center gap-1.5 border-b-2 px-2 py-2.5 font-heading text-xs font-extrabold uppercase tracking-[0.08em] ${
                isActive
                  ? 'border-accent text-accent'
                  : 'border-transparent text-muted hover:text-text'
              }`}
            >
              {tab.label}
              {count > 0 && (
                <span className={`px-1.5 py-0.5 text-[11px] font-bold leading-none tabular-nums ${
                  isActive
                    ? 'bg-accent text-bg'
                    : 'border border-divider text-muted'
                }`}>
                  {count}
                </span>
              )}
            </button>
          )
        })}
      </div>

      <div className="border-b border-divider p-2">
        <input
          type="text"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search transactions..."
          className="input"
        />
      </div>

      <div className="flex-1 overflow-y-auto">
        {loading && (
          <div className="py-8 text-center text-sm text-muted">Loading...</div>
        )}

        {!loading && error && (
          <div className="py-8 text-center text-sm text-accent-700">{error}</div>
        )}

        {!loading && !error && filteredTransactions.length === 0 && (
          <div className="py-8 text-center text-sm text-muted">
            {query ? 'No matching transactions' : 'No transactions'}
          </div>
        )}

        {!loading && !error && filteredTransactions.map((t) => (
          <TransactionRow
            key={t.id}
            transaction={t}
            draggable={activeTab === 'new'}
            showAssignment={activeTab === 'tracked'}
            onClick={activeTab !== 'pending' ? () => setEditingTransaction(t) : undefined}
            actions={
              // New-tab rows carry no delete control: the row is a drag source, and the edit
              // dialog already owns deleting a transaction.
              activeTab === 'deleted' ? (
                <div className="flex gap-3">
                  <button
                    onClick={(e) => { e.stopPropagation(); handleRestore(t.id) }}
                    className="font-heading text-xs font-bold uppercase tracking-wide text-accent hover:text-accent-700"
                  >
                    Restore
                  </button>
                  {t.isManual && (
                    <button
                      onClick={(e) => { e.stopPropagation(); handleHardDelete(t.id) }}
                      className="font-heading text-xs font-bold uppercase tracking-wide text-muted hover:text-accent-700"
                    >
                      Delete
                    </button>
                  )}
                </div>
              ) : undefined
            }
          />
        ))}
      </div>

      {activeTab === 'new' && (
        <div className="border-t-2 border-divider p-2">
          <button
            onClick={() => setEditingTransaction('create')}
            className="btn btn-ghost"
          >
            + Add Transaction
          </button>
        </div>
      )}

      {editingTransaction !== null && (
        <TransactionEditDialog
          transaction={editingTransaction === 'create' ? null : editingTransaction}
          onClose={() => setEditingTransaction(null)}
          onMutate={handleDialogMutate}
        />
      )}
    </div>
  )
}
