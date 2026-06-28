import { useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { useAppSelector } from '../../app/hooks'
import { api } from '../../api/client'
import type { TransactionResponse } from '../../types/transaction'
import { formatCurrency } from '../../utils/format'

interface PendingAssignment {
  lineItemId: number
  lineItemName: string
  amount: string
}

interface TransactionEditDialogProps {
  transaction: TransactionResponse | null
  onClose: () => void
  onMutate: () => void
}

function initAssignments(transaction: TransactionResponse | null): PendingAssignment[] {
  if (!transaction?.assignments?.length) {
    return []
  }
  return transaction.assignments.map((a) => ({
    lineItemId: a.lineItemId,
    lineItemName: a.lineItemName,
    amount: Math.abs(a.amount).toString(),
  }))
}

export default function TransactionEditDialog({ transaction, onClose, onMutate }: TransactionEditDialogProps) {
  const isCreate = !transaction
  const isEditable = isCreate || transaction.isManual
  const budget = useAppSelector((state) => state.budget.budget)

  const [description, setDescription] = useState(transaction?.description ?? '')
  const [isExpense, setIsExpense] = useState(transaction ? transaction.amount < 0 : true)
  const [amount, setAmount] = useState(transaction ? Math.abs(transaction.amount).toString() : '')
  const [date, setDate] = useState(transaction?.date?.slice(0, 10) ?? new Date().toISOString().split('T')[0])
  const [notes, setNotes] = useState(transaction?.notes ?? '')
  const [saving, setSaving] = useState(false)
  const [pendingAssignments, setPendingAssignments] = useState<PendingAssignment[]>(() => initAssignments(transaction))
  const [originalAssignments] = useState<PendingAssignment[]>(() => initAssignments(transaction))

  const parsedAmount = parseFloat(amount) || 0
  const absTotal = Math.abs(parsedAmount)
  const isSplit = pendingAssignments.length > 1

  const assignmentSum = pendingAssignments.reduce((sum, a) => sum + (parseFloat(a.amount) || 0), 0)
  const sumMatches = Math.abs(assignmentSum - absTotal) < 0.005

  const assignmentsValid = pendingAssignments.length === 0 ||
    pendingAssignments.length === 1 ||
    (isSplit && sumMatches && pendingAssignments.every((a) => parseFloat(a.amount) > 0))

  const selectedIds = new Set(pendingAssignments.map((a) => a.lineItemId))

  useEffect(() => {
    setPendingAssignments((prev) => {
      if (prev.length === 1) {
        return [{ ...prev[0], amount: absTotal.toString() }]
      }
      return prev
    })
  }, [absTotal])

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose()
      } else if (e.key === 'Enter' && !(e.target instanceof HTMLTextAreaElement)) {
        e.preventDefault()
        if (canSave && !saving) {
          handleSave()
        }
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  })

  const addAssignment = (lineItemId: number, lineItemName: string) => {
    setPendingAssignments((prev) => {
      if (prev.length === 0) {
        return [{ lineItemId, lineItemName, amount: absTotal.toString() }]
      }
      const remaining = absTotal - prev.reduce((sum, a) => sum + (parseFloat(a.amount) || 0), 0)
      return [...prev, { lineItemId, lineItemName, amount: remaining > 0 ? remaining.toFixed(2) : '' }]
    })
  }

  const removeAssignment = (lineItemId: number) => {
    setPendingAssignments((prev) => {
      const next = prev.filter((a) => a.lineItemId !== lineItemId)
      if (next.length === 1) {
        return [{ ...next[0], amount: absTotal.toString() }]
      }
      return next
    })
  }

  const updateAssignmentAmount = (lineItemId: number, newAmount: string) => {
    setPendingAssignments((prev) =>
      prev.map((a) => a.lineItemId === lineItemId ? { ...a, amount: newAmount } : a)
    )
  }

  const signedAmount = isExpense ? -absTotal : absTotal

  const hasFieldChanges = isCreate || (
    transaction && (
      description.trim() !== transaction.description ||
      signedAmount !== transaction.amount ||
      date !== transaction.date.slice(0, 10) ||
      (notes.trim() || null) !== (transaction.notes || null)
    )
  )

  const hasAssignmentChange = (() => {
    if (pendingAssignments.length !== originalAssignments.length) {
      return true
    }
    for (let i = 0; i < pendingAssignments.length; i++) {
      const p = pendingAssignments[i]
      const o = originalAssignments.find((a) => a.lineItemId === p.lineItemId)
      if (!o || Math.abs(parseFloat(p.amount) - parseFloat(o.amount)) > 0.005) {
        return true
      }
    }
    return false
  })()

  const hasChanges = hasFieldChanges || hasAssignmentChange
  const canSave = description.trim() && absTotal > 0 && hasChanges && assignmentsValid

  const handleSave = async () => {
    const desc = description.trim()
    if (!desc) {
      return
    }
    const rawAmount = parseFloat(amount)
    if (!rawAmount) {
      return
    }
    const txAmount = isExpense ? -Math.abs(rawAmount) : Math.abs(rawAmount)

    setSaving(true)

    let transactionId: number

    if (isCreate) {
      const result = await api.post<TransactionResponse>('/api/transactions', {
        date,
        description: desc,
        amount: txAmount,
        notes: notes.trim() || null,
      })
      if (result.error) {
        toast.error(result.error.message)
        setSaving(false)
        return
      }
      transactionId = result.data!.id
    } else {
      if (hasFieldChanges) {
        const result = await api.put<TransactionResponse>(`/api/transactions/${transaction.id}`, {
          date,
          description: desc,
          amount: txAmount,
          notes: notes.trim() || null,
        })
        if (result.error) {
          toast.error(result.error.message)
          setSaving(false)
          return
        }
      }
      transactionId = transaction.id
    }

    if (hasAssignmentChange) {
      const signedAssignments = pendingAssignments.map((a) => ({
        lineItemId: a.lineItemId,
        amount: Math.sign(txAmount) * Math.abs(parseFloat(a.amount) || 0),
      }))
      const result = await api.put<TransactionResponse>(
        `/api/transactions/${transactionId}/assignments`,
        { assignments: signedAssignments }
      )
      if (result.error) {
        toast.error(result.error.message)
        setSaving(false)
        return
      }
    }

    onMutate()
    onClose()
  }

  const handleDelete = async () => {
    if (!transaction) {
      return
    }
    const result = await api.delete<boolean>(`/api/transactions/${transaction.id}`)
    if (result.error) {
      toast.error(result.error.message)
    } else {
      onMutate()
      onClose()
    }
  }

  const hasDropdownOptions = budget?.groups.some((g) =>
    g.lineItems.some((li) => !selectedIds.has(li.id))
  )

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={onClose}>
      <div className="fixed inset-0 bg-black/50" />
      <div
        className="relative w-full max-w-md rounded-lg bg-white p-6 shadow-xl dark:bg-gray-800"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-white">
            {isCreate ? 'New Transaction' : 'Edit Transaction'}
          </h2>
          <button
            onClick={onClose}
            className="text-gray-400 hover:text-gray-600 dark:text-gray-500 dark:hover:text-gray-300"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5">
              <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
            </svg>
          </button>
        </div>

        <div className="flex flex-col gap-3">
          {isEditable ? (
            <div className="flex gap-4">
              <label className="flex items-center gap-1.5 text-sm text-gray-700 dark:text-gray-300">
                <input
                  type="radio"
                  checked={isExpense}
                  onChange={() => setIsExpense(true)}
                  className="accent-blue-600"
                />
                Expense
              </label>
              <label className="flex items-center gap-1.5 text-sm text-gray-700 dark:text-gray-300">
                <input
                  type="radio"
                  checked={!isExpense}
                  onChange={() => setIsExpense(false)}
                  className="accent-blue-600"
                />
                Income
              </label>
            </div>
          ) : (
            <div className="text-sm text-gray-500 dark:text-gray-400">
              {transaction.amount < 0 ? 'Expense' : 'Income'}
            </div>
          )}

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Description</label>
            {isEditable ? (
              <input
                type="text"
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                autoFocus
                className="w-full rounded border border-gray-300 px-3 py-1.5 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
              />
            ) : (
              <div className="text-sm text-gray-900 dark:text-white">{description}</div>
            )}
          </div>

          {transaction?.payee && (
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Payee</label>
              <div className="text-sm text-gray-900 dark:text-white">{transaction.payee}</div>
            </div>
          )}

          {transaction?.memo && (
            <div>
              <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Memo</label>
              <div className="text-sm text-gray-900 dark:text-white">{transaction.memo}</div>
            </div>
          )}

          <div className="flex gap-3">
            <div className="flex-1">
              <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Amount</label>
              {isEditable ? (
                <input
                  type="number"
                  value={amount}
                  onChange={(e) => setAmount(e.target.value)}
                  step="0.01"
                  className="w-full rounded border border-gray-300 px-3 py-1.5 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
                />
              ) : (
                <div className="text-sm text-gray-900 dark:text-white">{formatCurrency(parseFloat(amount))}</div>
              )}
            </div>
            <div className="flex-1">
              <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Date</label>
              {isEditable ? (
                <input
                  type="date"
                  value={date}
                  onChange={(e) => setDate(e.target.value)}
                  className="w-full rounded border border-gray-300 px-3 py-1.5 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
                />
              ) : (
                <div className="text-sm text-gray-900 dark:text-white">
                  {new Date(date + 'T00:00:00').toLocaleDateString()}
                </div>
              )}
            </div>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Notes</label>
            <textarea
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={2}
              className="w-full rounded border border-gray-300 px-3 py-1.5 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
            />
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-500 dark:text-gray-400">Line Item</label>
            {pendingAssignments.length > 0 && (
              <div className="mb-2 rounded border border-gray-200 dark:border-gray-600">
                {pendingAssignments.map((a) => (
                  <div key={a.lineItemId} className="flex items-center justify-between gap-2 border-b border-gray-100 px-3 py-1.5 last:border-b-0 dark:border-gray-700">
                    <span className="min-w-0 flex-1 truncate text-sm text-gray-900 dark:text-white">{a.lineItemName}</span>
                    {isSplit && (
                      <input
                        type="number"
                        value={a.amount}
                        onChange={(e) => updateAssignmentAmount(a.lineItemId, e.target.value)}
                        step="0.01"
                        className="w-24 rounded border border-gray-300 px-2 py-0.5 text-right text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
                      />
                    )}
                    <button
                      onClick={() => removeAssignment(a.lineItemId)}
                      className="text-gray-400 hover:text-red-500 dark:text-gray-500 dark:hover:text-red-400"
                      title="Remove"
                    >
                      <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4">
                        <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
                      </svg>
                    </button>
                  </div>
                ))}
              </div>
            )}
            {isSplit && !sumMatches && (
              <p className="mb-2 text-xs text-red-500">
                Split amounts must add up to {formatCurrency(absTotal)}
              </p>
            )}
            {hasDropdownOptions && (
              <select
                value=""
                onChange={(e) => {
                  const id = parseInt(e.target.value)
                  if (!id || !budget) {
                    return
                  }
                  for (const group of budget.groups) {
                    const li = group.lineItems.find((l) => l.id === id)
                    if (li) {
                      addAssignment(li.id, li.name)
                      return
                    }
                  }
                }}
                className="w-full rounded border border-gray-300 px-3 py-1.5 text-sm dark:border-gray-600 dark:bg-gray-700 dark:text-white"
              >
                <option value="">Select a line item...</option>
                {budget!.groups.map((group) => {
                  const available = group.lineItems.filter((li) => !selectedIds.has(li.id))
                  if (available.length === 0) {
                    return null
                  }
                  return (
                    <optgroup key={group.id} label={group.name}>
                      {available.map((li) => (
                        <option key={li.id} value={li.id}>{li.name}</option>
                      ))}
                    </optgroup>
                  )
                })}
              </select>
            )}
          </div>
        </div>

        <div className="mt-5 flex items-center justify-between">
          <div className="flex gap-2">
            {!isCreate && originalAssignments.length === 0 && pendingAssignments.length === 0 && (
              <button
                onClick={handleDelete}
                className="rounded border border-red-300 px-3 py-1.5 text-sm font-medium text-red-600 hover:bg-red-50 dark:border-red-600 dark:text-red-400 dark:hover:bg-red-900/20"
              >
                Delete
              </button>
            )}
          </div>
          <div className="flex gap-2">
            <button
              onClick={onClose}
              className="rounded px-3 py-1.5 text-sm text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
            >
              Cancel
            </button>
            {(isCreate || isEditable || hasAssignmentChange || notes.trim() !== (transaction?.notes ?? '')) && (
              <button
                onClick={handleSave}
                disabled={!canSave || saving}
                className="rounded bg-blue-600 px-4 py-1.5 text-sm font-medium text-white hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {saving ? 'Saving...' : 'Save'}
              </button>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
