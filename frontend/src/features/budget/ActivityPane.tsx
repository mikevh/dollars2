import { useEffect, useState } from 'react'
import { api } from '../../api/client'
import type { LineItemResponse } from '../../types/budget'
import type { TransactionResponse } from '../../types/transaction'
import { formatCurrency } from '../../utils/format'
import TransactionRow from '../transactions/TransactionRow'
import TransactionEditDialog from '../transactions/TransactionEditDialog'

interface ActivityPaneProps {
  lineItem: LineItemResponse
  isIncome: boolean
  budgetMonth: number
  onClose: () => void
  onBudgetMutate?: () => void
}

const monthNames = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']

export default function ActivityPane({ lineItem, isIncome, budgetMonth, onClose, onBudgetMutate }: ActivityPaneProps) {
  const [transactions, setTransactions] = useState<TransactionResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [editingTransaction, setEditingTransaction] = useState<TransactionResponse | null>(null)

  const spent = isIncome ? lineItem.receivedAmount : lineItem.spentAmount
  const rollover = isIncome ? 0 : lineItem.rolloverAmount
  const remaining = lineItem.plannedAmount + rollover - spent

  const fetchActivity = () => {
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
  }

  useEffect(() => {
    fetchActivity()
  }, [lineItem.id])

  const handleDialogMutate = () => {
    fetchActivity()
    onBudgetMutate?.()
  }

  return (
    <div className="flex h-full flex-col">
      <div className="flex items-center justify-between border-b-2 border-divider px-4 py-3">
        <h2 className="font-heading text-sm font-extrabold uppercase tracking-wide text-text">{lineItem.name}</h2>
        <button
          onClick={onClose}
          className="text-muted hover:text-accent-700"
          title="Close"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5">
            <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
          </svg>
        </button>
      </div>

      <div className="flex justify-between border-b border-divider px-4 py-2 text-xs">
        <div>
          <span className="font-heading text-[11px] font-bold uppercase tracking-[0.08em] text-muted">Planned </span>
          <span className="font-medium tabular-nums text-text">{formatCurrency(lineItem.plannedAmount)}</span>
        </div>
        <div>
          <span className="font-heading text-[11px] font-bold uppercase tracking-[0.08em] text-muted">{isIncome ? 'Received' : 'Spent'} </span>
          <span className="font-medium tabular-nums text-text">{formatCurrency(spent)}</span>
        </div>
        {rollover !== 0 && (
          <div>
            <span className="font-heading text-[11px] font-bold uppercase tracking-[0.08em] text-muted">Rollover </span>
            <span className={`font-medium tabular-nums ${rollover < 0 ? 'text-accent-700' : 'text-text'}`}>
              {formatCurrency(rollover)}
            </span>
          </div>
        )}
        <div>
          <span className="font-heading text-[11px] font-bold uppercase tracking-[0.08em] text-muted">Remaining </span>
          <span className={`font-medium tabular-nums ${remaining < 0 ? 'text-accent-700' : 'text-text'}`}>
            {formatCurrency(remaining)}
          </span>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto">
        {loading && (
          <div className="py-8 text-center text-sm text-muted">Loading...</div>
        )}

        {!loading && error && (
          <div className="py-8 text-center text-sm text-accent-700">{error}</div>
        )}

        {!loading && !error && transactions.length === 0 && rollover === 0 && (
          <div className="py-8 text-center text-sm text-muted">No transactions</div>
        )}

        {!loading && !error && rollover !== 0 && (
          <div className="flex items-center justify-between border-b border-divider px-4 py-2">
            <span className="text-sm italic text-muted">Rollover from {monthNames[(budgetMonth - 2 + 12) % 12]}</span>
            <span className={`text-sm font-medium tabular-nums ${rollover > 0 ? 'text-text' : 'text-accent-700'}`}>
              {rollover > 0 ? '+' : '-'}{formatCurrency(Math.abs(rollover))}
            </span>
          </div>
        )}

        {!loading && !error && transactions.map((t) => {
          const assignment = t.assignments.find((a) => a.lineItemId === lineItem.id)
          return (
            <TransactionRow
              key={t.id}
              transaction={t}
              displayAmount={assignment ? assignment.amount : undefined}
              onClick={() => setEditingTransaction(t)}
            />
          )
        })}
      </div>

      {editingTransaction && (
        <TransactionEditDialog
          transaction={editingTransaction}
          onClose={() => setEditingTransaction(null)}
          onMutate={handleDialogMutate}
        />
      )}
    </div>
  )
}
