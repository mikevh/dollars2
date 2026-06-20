import { useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import {
  fetchNewTransactions,
  fetchTrackedTransactions,
  fetchDeletedTransactions,
  fetchPendingTransactions,
  createTransaction,
  softDeleteTransaction,
  restoreTransaction,
  hardDeleteTransaction,
  setActiveTab,
} from './transactionSlice'
import TransactionRow from './TransactionRow'

const tabs = [
  { key: 'new' as const, label: 'New' },
  { key: 'tracked' as const, label: 'Tracked' },
  { key: 'deleted' as const, label: 'Deleted' },
  { key: 'pending' as const, label: 'Pending' },
]

export default function TransactionPane() {
  const dispatch = useAppDispatch()
  const { transactions, loading, error, activeTab } = useAppSelector((state) => state.transactions)
  const [adding, setAdding] = useState(false)
  const [newDesc, setNewDesc] = useState('')
  const [newAmount, setNewAmount] = useState('')
  const [newDate, setNewDate] = useState(new Date().toISOString().split('T')[0])

  useEffect(() => {
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

  const handleAdd = async () => {
    const description = newDesc.trim()
    if (!description) {
      return
    }
    const amount = parseFloat(newAmount) || 0
    const result = await dispatch(createTransaction({ date: newDate, description, amount }))
    if (createTransaction.rejected.match(result)) {
      toast.error(result.payload as string)
    } else {
      setNewDesc('')
      setNewAmount('')
      setNewDate(new Date().toISOString().split('T')[0])
      setAdding(false)
    }
  }

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

  return (
    <div className="flex h-full flex-col">
      <div className="flex border-b border-gray-200 dark:border-gray-700">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            onClick={() => dispatch(setActiveTab(tab.key))}
            className={`flex-1 px-4 py-2 text-sm font-medium ${
              activeTab === tab.key
                ? 'border-b-2 border-blue-600 text-blue-600 dark:border-blue-400 dark:text-blue-400'
                : 'text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200'
            }`}
          >
            {tab.label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-y-auto">
        {loading && (
          <div className="py-8 text-center text-sm text-gray-500 dark:text-gray-400">Loading...</div>
        )}

        {!loading && error && (
          <div className="py-8 text-center text-sm text-red-500">{error}</div>
        )}

        {!loading && !error && transactions.length === 0 && (
          <div className="py-8 text-center text-sm text-gray-400 dark:text-gray-500">No transactions</div>
        )}

        {!loading && !error && transactions.map((t) => (
          <TransactionRow
            key={t.id}
            transaction={t}
            actions={
              <>
                {activeTab === 'new' && (
                  <button
                    onClick={() => handleSoftDelete(t.id)}
                    className="text-gray-400 hover:text-red-500 dark:text-gray-500 dark:hover:text-red-400"
                    title="Delete"
                  >
                    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4">
                      <path fillRule="evenodd" d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.519.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z" clipRule="evenodd" />
                    </svg>
                  </button>
                )}
                {activeTab === 'deleted' && (
                  <div className="flex gap-1">
                    <button
                      onClick={() => handleRestore(t.id)}
                      className="text-xs text-blue-600 hover:text-blue-700 dark:text-blue-400 dark:hover:text-blue-300"
                    >
                      Restore
                    </button>
                    {t.isManual && (
                      <button
                        onClick={() => handleHardDelete(t.id)}
                        className="text-xs text-red-500 hover:text-red-600"
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
        <div className="border-t border-gray-200 p-3 dark:border-gray-700">
          {adding ? (
            <div className="flex flex-col gap-2">
              <div className="flex gap-2">
                <input
                  type="text"
                  value={newDesc}
                  onChange={(e) => setNewDesc(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      handleAdd()
                    } else if (e.key === 'Escape') {
                      setAdding(false)
                    }
                  }}
                  placeholder="Description"
                  autoFocus
                  className="flex-1 rounded border border-gray-300 px-2 py-1 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
                />
                <input
                  type="number"
                  value={newAmount}
                  onChange={(e) => setNewAmount(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      handleAdd()
                    } else if (e.key === 'Escape') {
                      setAdding(false)
                    }
                  }}
                  placeholder="0.00"
                  step="0.01"
                  className="w-24 rounded border border-gray-300 px-2 py-1 text-right text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
                />
              </div>
              <div className="flex items-center gap-2">
                <input
                  type="date"
                  value={newDate}
                  onChange={(e) => setNewDate(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Escape') {
                      setAdding(false)
                    }
                  }}
                  className="rounded border border-gray-300 px-2 py-1 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
                />
                <div className="flex-1" />
                <button
                  onClick={handleAdd}
                  disabled={!newDesc.trim() || !parseFloat(newAmount)}
                  className="rounded bg-blue-600 px-3 py-1 text-sm text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  Add
                </button>
              </div>
            </div>
          ) : (
            <button
              onClick={() => setAdding(true)}
              className="text-sm font-medium text-blue-600 hover:text-blue-700 dark:text-blue-400 dark:hover:text-blue-300"
            >
              + Add Transaction
            </button>
          )}
        </div>
      )}
    </div>
  )
}
