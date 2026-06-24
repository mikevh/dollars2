import { useDraggable } from '@dnd-kit/core'
import type { TransactionResponse } from '../../types/transaction'
import { formatCurrency } from '../../utils/format'

interface TransactionRowProps {
  transaction: TransactionResponse
  actions?: React.ReactNode
  draggable?: boolean
  onClick?: () => void
  displayAmount?: number
}

export default function TransactionRow({ transaction, actions, draggable, onClick, displayAmount }: TransactionRowProps) {
  const shownAmount = displayAmount ?? transaction.amount
  const isIncome = shownAmount > 0

  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: `transaction-${transaction.id}`,
    data: { transaction },
    disabled: !draggable,
  })

  return (
    <div
      ref={setNodeRef}
      {...(draggable ? { ...listeners, ...attributes } : {})}
      onClick={onClick}
      className={`flex items-center justify-between border-b border-gray-100 px-4 py-2 last:border-b-0 dark:border-gray-700 ${
        draggable ? 'cursor-grab active:cursor-grabbing' : onClick ? 'cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-700/50' : ''
      } ${isDragging ? 'opacity-50' : ''}`}
    >
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm text-gray-900 dark:text-white">{transaction.description}</div>
        <div className="flex gap-2 text-xs text-gray-400 dark:text-gray-500">
          <span>{new Date(transaction.date.slice(0, 10) + 'T00:00:00').toLocaleDateString()}</span>
          {transaction.isManual && <span>manual</span>}
        </div>
      </div>
      <div className="flex items-center gap-3">
        <span className={`text-sm font-medium ${isIncome ? 'text-green-600 dark:text-green-400' : 'text-gray-900 dark:text-white'}`}>
          {isIncome ? '+' : '-'}{formatCurrency(Math.abs(shownAmount))}
        </span>
        {actions}
      </div>
    </div>
  )
}
