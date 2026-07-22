import { useDraggable } from '@dnd-kit/core'
import type { TransactionResponse } from '../../types/transaction'
import { formatCurrency } from '../../utils/format'

interface TransactionRowProps {
  transaction: TransactionResponse
  actions?: React.ReactNode
  draggable?: boolean
  onClick?: () => void
  displayAmount?: number
  showAssignment?: boolean
}

export default function TransactionRow({ transaction, actions, draggable, onClick, displayAmount, showAssignment }: TransactionRowProps) {
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
      className={`flex items-center justify-between border-b border-divider px-4 py-2 last:border-b-0 ${
        draggable ? 'cursor-grab active:cursor-grabbing' : onClick ? 'cursor-pointer hover:bg-[color-mix(in_srgb,var(--color-text)_6%,transparent)]' : ''
      } ${isDragging ? 'opacity-50' : ''}`}
    >
      <div className="min-w-0 flex-1">
        <div className="truncate text-sm text-text">{transaction.payee || transaction.description}</div>
        <div className="flex gap-2 text-xs text-muted">
          <span>{new Date(transaction.date.slice(0, 10) + 'T00:00:00').toLocaleDateString()}</span>
          {transaction.isManual && (
            <span className="border border-divider px-1 uppercase tracking-wide">manual</span>
          )}
          {showAssignment && transaction.assignments.length > 0 && (
            <span className="truncate">{transaction.assignments.map((a) => a.lineItemName).join(', ')}</span>
          )}
        </div>
      </div>
      <div className="flex items-center gap-3">
        <span className={`text-sm tabular-nums text-text ${isIncome ? 'font-bold' : 'font-medium'}`}>
          {isIncome ? '+' : '-'}{formatCurrency(Math.abs(shownAmount))}
        </span>
        {actions}
      </div>
    </div>
  )
}
