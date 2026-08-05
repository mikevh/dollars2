import { useEffect, useId, useRef, useState } from 'react'
import toast from 'react-hot-toast'
import { useAppSelector } from '../../app/hooks'
import { api } from '../../api/client'
import Dialog, { DialogHeader } from '../../components/Dialog'
import type { TransactionResponse } from '../../types/transaction'
import { formatCurrency } from '../../utils/format'
import { isMoneyDraft } from '../../utils/money'

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

export default function TransactionEditDialog({ transaction, onClose, onMutate }: TransactionEditDialogProps) 
{
  const isCreate = !transaction
  const isEditable = isCreate || transaction.isManual
  // A fetch in flight (or a rejected fetch) leaves the previous month's budget in the
  // store rather than nulling it out (see budgetSlice.ts), so gate on it actually
  // matching the month being viewed — otherwise this dropdown could assign the
  // transaction to a line item from a different month's budget.
  const { budget: rawBudget, currentYear, currentMonth } = useAppSelector((state) => state.budget)
  const budget = rawBudget && rawBudget.year === currentYear && rawBudget.month === currentMonth
    ? rawBudget
    : null
  const titleId = useId()
  // Null when the transaction isn't editable and no input renders — Dialog then
  // falls back to focusing the panel.
  const descriptionRef = useRef<HTMLInputElement>(null)

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

  // A lone assignment always carries the full transaction amount. Re-seeding it
  // during render (rather than in an effect) is React's recommended way to reset
  // state on an input change and avoids a cascading re-render.
  const [seededTotal, setSeededTotal] = useState(absTotal)
  if (seededTotal !== absTotal) {
    setSeededTotal(absTotal)
    if (pendingAssignments.length === 1) {
      setPendingAssignments([{ ...pendingAssignments[0], amount: absTotal.toString() }])
    }
  }

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

  // Flipping expense <-> income keeps the assignment magnitudes but flips their signs, so we must
  // re-send the assignments to re-sign the stored rows. Comparing magnitudes alone would miss this.
  const assignmentSignFlipped = !isCreate && pendingAssignments.length > 0 &&
    Math.sign(signedAmount) !== Math.sign(transaction!.amount)

  const hasAssignmentChange = (() => {
    if (assignmentSignFlipped) {
      return true
    }
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

  // Escape is handled by Dialog; this listener covers only Enter-to-save. It is
  // attached once on mount and reads the current handler through a ref, instead of
  // closing over save state and re-subscribing on every render the way an effect
  // with no dependency array would.
  const latestKeyDown = useRef<(e: KeyboardEvent) => void>(() => {})
  useEffect(() => {
    latestKeyDown.current = (e: KeyboardEvent) => {
      // Buttons are excluded as well as textareas: now that the dialog traps focus,
      // Cancel/Close/Delete are reachable by keyboard, and swallowing Enter there
      // would save instead of activating the focused button.
      if (
        e.key === 'Enter' &&
        !(e.target instanceof HTMLTextAreaElement) &&
        !(e.target instanceof HTMLButtonElement)
      ) {
        e.preventDefault()
        if (canSave && !saving) {
          handleSave()
        }
      }
    }
  })

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => latestKeyDown.current(e)
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [])

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
    <Dialog onClose={onClose} labelledBy={titleId} initialFocusRef={descriptionRef} className="max-w-md">
      <DialogHeader
        id={titleId}
        title={isCreate ? 'New Transaction' : 'Edit Transaction'}
        onClose={onClose}
      />

      <div className="flex flex-col gap-3">
        {isEditable ? (
          <div className="flex gap-4">
            <label className="flex items-center gap-1.5 text-sm text-text">
              <input
                type="radio"
                checked={isExpense}
                onChange={() => setIsExpense(true)}
                className="accent-accent"
              />
              Expense
            </label>
            <label className="flex items-center gap-1.5 text-sm text-text">
              <input
                type="radio"
                checked={!isExpense}
                onChange={() => setIsExpense(false)}
                className="accent-accent"
              />
              Income
            </label>
          </div>
        ) : (
          <div className="text-sm text-muted">
            {transaction.amount < 0 ? 'Expense' : 'Income'}
          </div>
        )}

        <div className="field">
          <label>Description</label>
          {isEditable ? (
            <input
              ref={descriptionRef}
              type="text"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              className="input"
            />
          ) : (
            <div className="text-sm text-text">{description}</div>
          )}
        </div>

        {transaction?.payee && (
          <div className="field">
            <label>Payee</label>
            <div className="text-sm text-text">{transaction.payee}</div>
          </div>
        )}

        {transaction?.memo && (
          <div className="field">
            <label>Memo</label>
            <div className="text-sm text-text">{transaction.memo}</div>
          </div>
        )}

        <div className="flex gap-3">
          <div className="field flex-1">
            <label>Amount</label>
            {isEditable ? (
              <input
                type="text"
                inputMode="decimal"
                aria-label="Amount"
                value={amount}
                onChange={(e) => {
                  if (isMoneyDraft(e.target.value)) {
                    setAmount(e.target.value)
                  }
                }}
                className="input"
              />
            ) : (
              <div className="text-sm text-text">{formatCurrency(parseFloat(amount))}</div>
            )}
          </div>
          <div className="field flex-1">
            <label>Date</label>
            {isEditable ? (
              <input
                type="date"
                value={date}
                onChange={(e) => setDate(e.target.value)}
                className="input"
              />
            ) : (
              <div className="text-sm text-text">
                {new Date(date + 'T00:00:00').toLocaleDateString()}
              </div>
            )}
          </div>
        </div>

        <div className="field">
          <label>Notes</label>
          <textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={2}
            className="input"
          />
        </div>

        <div className="field">
          <label>Line Item</label>
          {pendingAssignments.length > 0 && (
            <div className="mb-2 border border-divider">
              {pendingAssignments.map((a) => (
                <div key={a.lineItemId} className="flex items-center justify-between gap-2 border-b border-divider px-3 py-1.5 last:border-b-0">
                  <span className="min-w-0 flex-1 truncate text-sm text-text">{a.lineItemName}</span>
                  {isSplit && (
                    <input
                      type="text"
                      inputMode="decimal"
                      aria-label={`${a.lineItemName} amount`}
                      value={a.amount}
                      onChange={(e) => {
                        if (isMoneyDraft(e.target.value)) {
                          updateAssignmentAmount(a.lineItemId, e.target.value)
                        }
                      }}
                      className="input w-24 text-right"
                    />
                  )}
                  <button
                    onClick={() => removeAssignment(a.lineItemId)}
                    className="text-muted hover:text-accent-700"
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
            <p className="mb-2 text-xs text-accent-700">
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
              className="input"
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
              className="btn btn-secondary text-accent-700"
            >
              Delete
            </button>
          )}
        </div>
        <div className="flex gap-2">
          <button
            onClick={onClose}
            className="btn btn-secondary"
          >
            Cancel
          </button>
          {(isCreate || isEditable || hasAssignmentChange || notes.trim() !== (transaction?.notes ?? '')) && (
            <button
              onClick={handleSave}
              disabled={!canSave || saving}
              className="btn btn-primary"
            >
              {saving ? 'Saving...' : 'Save'}
            </button>
          )}
        </div>
      </div>
    </Dialog>
  )
}
