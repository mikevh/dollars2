import { useEffect } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft } from '@fortawesome/free-solid-svg-icons'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import {
  fetchAccountTransactions,
  clearAccountTransactions,
} from '../features/accountTransactions/accountTransactionsSlice'
import { formatCurrency } from '../utils/format'
import type { TransactionResponse } from '../types/transaction'

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

export default function AccountTransactionsPage() {
  const dispatch = useAppDispatch()
  const { accountId } = useParams<{ accountId: string }>()
  const { data, loading, error } = useAppSelector((state) => state.accountTransactions)

  useEffect(() => {
    const id = Number(accountId)
    if (!Number.isNaN(id)) {
      dispatch(fetchAccountTransactions(id))
    }
    return () => {
      dispatch(clearAccountTransactions())
    }
  }, [dispatch, accountId])

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
        {loading && <div className="text-muted py-12 text-center">Loading...</div>}

        {!loading && error && <div className="py-12 text-center text-accent">{error}</div>}

        {!loading && !error && data && data.transactions.length === 0 && (
          <div className="text-muted py-12 text-center">No transactions for this account.</div>
        )}

        {!loading && !error && data && data.transactions.length > 0 && (
          <div className="overflow-x-auto border border-divider bg-surface shadow-elev-sm">
            <table className="w-full text-[14px]">
              <thead>
                <tr className="text-muted border-b-2 border-divider text-left text-[12px] font-semibold uppercase tracking-wide">
                  <th className="px-4 py-2 font-semibold">Date</th>
                  <th className="px-4 py-2 font-semibold">Description</th>
                  <th className="px-4 py-2 text-right font-semibold">Amount</th>
                  <th className="px-4 py-2 font-semibold">Budget item</th>
                </tr>
              </thead>
              <tbody>
                {data.transactions.map((t) => (
                  <tr
                    key={t.id}
                    className={`border-b border-divider last:border-b-0 ${t.isDeleted ? 'text-muted line-through' : ''}`}
                  >
                    <td className="whitespace-nowrap px-4 py-2 tabular-nums">{formatDate(t.date)}</td>
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
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
