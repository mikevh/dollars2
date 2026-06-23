import { useEffect, useState } from 'react'
import { api } from '../../api/client'
import type { LineItemResponse } from '../../types/budget'
import type { TransactionResponse } from '../../types/transaction'
import { formatCurrency } from '../../utils/format'
import TransactionRow from '../transactions/TransactionRow'

interface ActivityPaneProps {
  lineItem: LineItemResponse
  isIncome: boolean
  onClose: () => void
}

export default function ActivityPane({ lineItem, isIncome, onClose }: ActivityPaneProps) {
  const [transactions, setTransactions] = useState<TransactionResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const spent = isIncome ? lineItem.receivedAmount : lineItem.spentAmount
  const remaining = lineItem.plannedAmount - spent

  useEffect(() => {
    setLoading(true)
    setError(null)
    api.get<TransactionResponse[]>(`/api/line-items/${lineItem.id}/activity`).then((result) => {
      if (result.error) {
        setError(result.error.message)
      } else {
        setTransactions(result.data!)
      }
      setLoading(false)
    })
  }, [lineItem.id])

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between border-b border-gray-200 px-4 py-3 dark:border-gray-700">
        <h2 className="text-sm font-semibold text-gray-900 dark:text-white">{lineItem.name}</h2>
        <button
          onClick={onClose}
          className="text-gray-400 hover:text-gray-600 dark:text-gray-500 dark:hover:text-gray-300"
          title="Close"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5">
            <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
          </svg>
        </button>
      </div>

      <div className="flex justify-between border-b border-gray-200 px-4 py-2 text-xs dark:border-gray-700">
        <div>
          <span className="text-gray-400 dark:text-gray-500">Planned </span>
          <span className="font-medium text-gray-700 dark:text-gray-300">{formatCurrency(lineItem.plannedAmount)}</span>
        </div>
        <div>
          <span className="text-gray-400 dark:text-gray-500">{isIncome ? 'Received' : 'Spent'} </span>
          <span className="font-medium text-gray-700 dark:text-gray-300">{formatCurrency(spent)}</span>
        </div>
        <div>
          <span className="text-gray-400 dark:text-gray-500">Remaining </span>
          <span className={`font-medium ${remaining < 0 ? 'text-red-500' : 'text-gray-700 dark:text-gray-300'}`}>
            {formatCurrency(remaining)}
          </span>
        </div>
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
          <TransactionRow key={t.id} transaction={t} />
        ))}
      </div>
    </div>
  )
}
