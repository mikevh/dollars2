import type { TransactionResponse } from '../../types/transaction'
import { formatCurrency } from '../../utils/format'

interface TransactionRowProps {
  transaction: TransactionResponse
  actions?: React.ReactNode
}

export default function TransactionRow({ transaction, actions }: TransactionRowProps) {
  const isIncome = transaction.amount > 0

  return (
    <div className="flex items-center justify-between border-b border-gray-100 px-4 py-2 last:border-b-0 dark:border-gray-700">
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm text-gray-900 dark:text-white">{transaction.description}</div>
        <div className="flex gap-2 text-xs text-gray-400 dark:text-gray-500">
          <span>{new Date(transaction.date.slice(0, 10) + 'T00:00:00').toLocaleDateString()}</span>
          {transaction.isManual && <span>manual</span>}
        </div>
      </div>
      <div className="flex items-center gap-3">
        <span className={`text-sm font-medium ${isIncome ? 'text-green-600 dark:text-green-400' : 'text-gray-900 dark:text-white'}`}>
          {isIncome ? '+' : '-'}{formatCurrency(Math.abs(transaction.amount))}
        </span>
        {actions}
      </div>
    </div>
  )
}
