import { useState, useEffect } from 'react'
import toast from 'react-hot-toast'
import { useDroppable } from '@dnd-kit/core'
import type { LineItemResponse } from '../../types/budget'
import { formatCurrency } from '../../utils/format'
import { useAppDispatch } from '../../app/hooks'
import { updateLineItem, deleteLineItem } from './budgetSlice'

interface LineItemRowProps {
  lineItem: LineItemResponse
  groupId: number
  isIncome: boolean
  startEditing?: boolean
  onEditComplete?: () => void
  onSelect?: () => void
}

export default function LineItemRow({ lineItem, groupId, isIncome, startEditing, onEditComplete, onSelect }: LineItemRowProps) {
  const dispatch = useAppDispatch()
  const { isOver, setNodeRef } = useDroppable({
    id: `lineitem-${lineItem.id}`,
    data: { lineItemId: lineItem.id },
  })
  const [editingName, setEditingName] = useState(false)
  const [editingAmount, setEditingAmount] = useState(false)
  const [nameValue, setNameValue] = useState(lineItem.name)
  const [amountValue, setAmountValue] = useState(lineItem.plannedAmount.toString())

  useEffect(() => {
    if (startEditing) {
      setEditingName(true)
      setNameValue('')
    }
  }, [startEditing])

  const remaining = isIncome
    ? lineItem.plannedAmount - lineItem.receivedAmount
    : lineItem.plannedAmount + lineItem.rolloverAmount - lineItem.spentAmount

  const saveUpdate = async (name: string, plannedAmount: number) => {
    if (name === lineItem.name && plannedAmount === lineItem.plannedAmount) {
      return
    }
    const result = await dispatch(updateLineItem({ lineItemId: lineItem.id, groupId, name, plannedAmount }))
    if (updateLineItem.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  const handleSaveName = async () => {
    const trimmed = nameValue.trim()
    if (!trimmed) {
      setNameValue(lineItem.name)
      setEditingName(false)
      onEditComplete?.()
      return
    }
    await saveUpdate(trimmed, lineItem.plannedAmount)
    setEditingName(false)
    if (startEditing) {
      setEditingAmount(true)
      onEditComplete?.()
    }
  }

  const handleSaveAmount = async () => {
    const parsed = parseFloat(amountValue) || 0
    const rounded = Math.round(parsed * 100) / 100
    setAmountValue(rounded.toString())
    await saveUpdate(lineItem.name, rounded)
    setEditingAmount(false)
  }

  const handleDelete = async () => {
    const result = await dispatch(deleteLineItem({ lineItemId: lineItem.id, groupId }))
    if (deleteLineItem.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  return (
    <div
      ref={setNodeRef}
      onClick={(e) => { e.stopPropagation(); onSelect?.() }}
      className={`flex h-11 cursor-pointer items-center justify-between border-b border-divider px-4 last:border-b-0 hover:bg-[color-mix(in_srgb,var(--color-text)_6%,transparent)] ${
        isOver ? 'bg-accent-100 ring-1 ring-inset ring-accent' : ''
      }`}
    >
      <div className="flex items-center gap-2">
        {editingName ? (
          <input
            type="text"
            value={nameValue}
            onChange={(e) => setNameValue(e.target.value)}
            onClick={(e) => e.stopPropagation()}
            onBlur={handleSaveName}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                handleSaveName()
              } else if (e.key === 'Escape') {
                setEditingName(false)
                setNameValue(lineItem.name)
              }
            }}
            autoFocus
            className="border border-divider bg-surface px-2 py-0.5 text-sm text-text"
          />
        ) : (
          <span
            onClick={(e) => { e.stopPropagation(); setEditingName(true) }}
            className="cursor-text text-sm text-text hover:text-accent-700"
          >
            {lineItem.name}
          </span>
        )}
        {editingName && <button
          onMouseDown={(e) => e.preventDefault()}
          onClick={(e) => { e.stopPropagation(); handleDelete() }}
          className="text-muted hover:text-accent-700"
          title="Delete item"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-3.5 w-3.5">
            <path fillRule="evenodd" d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.519.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z" clipRule="evenodd" />
          </svg>
        </button>}
      </div>
      <div className="flex gap-6 text-sm tabular-nums">
        {editingAmount ? (
          <input
            type="number"
            value={amountValue}
            onChange={(e) => setAmountValue(e.target.value)}
            onClick={(e) => e.stopPropagation()}
            onBlur={handleSaveAmount}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                handleSaveAmount()
              } else if (e.key === 'Escape') {
                setEditingAmount(false)
                setAmountValue(lineItem.plannedAmount.toString())
              }
            }}
            step="0.01"
            autoFocus
            className="w-24 border border-divider bg-surface px-2 py-0.5 text-right text-sm text-text"
          />
        ) : (
          <span
            onClick={(e) => { e.stopPropagation(); setEditingAmount(true) }}
            className="w-24 cursor-text border border-transparent px-2 py-0.5 text-right text-text hover:text-accent-700"
          >
            {formatCurrency(lineItem.plannedAmount)}
          </span>
        )}
        <span className="w-24 border border-transparent px-2 py-0.5 text-right text-muted">
          {formatCurrency(isIncome ? lineItem.receivedAmount : lineItem.spentAmount)}
        </span>
        <span className={`w-24 border border-transparent px-2 py-0.5 text-right ${remaining < 0 ? 'text-accent-700' : 'text-text'}`}>
          {formatCurrency(remaining)}
        </span>
      </div>
    </div>
  )
}
