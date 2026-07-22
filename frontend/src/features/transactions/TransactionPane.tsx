import { useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import {
  fetchNewTransactions,
  fetchTrackedTransactions,
  fetchDeletedTransactions,
  fetchPendingTransactions,
  fetchCounts,
  softDeleteTransaction,
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

  const query = search.trim().toLowerCase()
  const filteredTransactions = query
    ? transactions.filter((t) => {
        const fields = [t.payee, t.description, t.memo, Math.abs(t.amount).toFixed(2)]
        return fields.some((field) => field?.toLowerCase().includes(query))
      })
    : transactions

  const fetchCurrentTab = () => {
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
  }

  useEffect(() => {
    fetchCurrentTab()
  }, [dispatch, activeTab, currentYear, currentMonth])

  useEffect(() => {
    setSearch('')
  }, [activeTab])

  const handleSoftDelete = async (id: number) => {
    const result = await dispatch(softDeleteTransaction({ id }))
    if (softDeleteTransaction.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

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
              className={`flex flex-1 items-center justify-center gap-1.5 border-b-2 px-4 py-2.5 font-heading text-xs font-extrabold uppercase tracking-[0.08em] ${
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
              <>
                {activeTab === 'new' && (
                  <button
                    onClick={(e) => { e.stopPropagation(); handleSoftDelete(t.id) }}
                    className="text-muted hover:text-accent-700"
                    title="Delete"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4">
                      <path fillRule="evenodd" d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.519.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z" clipRule="evenodd" />
                    </svg>
                  </button>
                )}
                {activeTab === 'deleted' && (
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
                )}
              </>
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
