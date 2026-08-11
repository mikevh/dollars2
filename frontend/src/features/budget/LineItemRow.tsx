import { useState } from 'react'
import toast from 'react-hot-toast'
import { useDroppable } from '@dnd-kit/core'
import type { LineItemResponse } from '../../types/budget'
import { formatCurrency } from '../../utils/format'
import { isMoneyDraft } from '../../utils/money'
import { useAppDispatch } from '../../app/hooks'
import { useFocusSelectOnOpen } from '../../hooks/useFocusSelectOnOpen'
import { updateLineItem, deleteLineItem } from './budgetSlice'
import { GROUP_GRID_COLUMNS, type GroupMetric } from './groupGridColumns'
import { lineItemActual, lineItemRemaining } from './lineItemAmounts'
import FlowAmount from './FlowAmount'

interface LineItemRowProps {
  lineItem: LineItemResponse
  groupId: number
  metric: GroupMetric
  isSelected?: boolean
  startEditing?: boolean
  onEditComplete?: () => void
  onSelect?: () => void
}

export default function LineItemRow({ lineItem, groupId, metric, isSelected, startEditing, onEditComplete, onSelect }: LineItemRowProps) {
  const isIncome = lineItem.isIncome
  const dispatch = useAppDispatch()
  const { isOver, setNodeRef } = useDroppable({
    id: `lineitem-${lineItem.id}`,
    data: { lineItemId: lineItem.id },
  })
  // A freshly added item mounts with startEditing set, so seed the name editor open
  // and empty rather than opening it from an effect after the first paint.
  const [editingName, setEditingName] = useState(startEditing ?? false)
  const [editingAmount, setEditingAmount] = useState(false)
  const [nameValue, setNameValue] = useState(startEditing ? '' : lineItem.name)
  const [amountValue, setAmountValue] = useState(lineItem.plannedAmount.toString())
  const nameInputRef = useFocusSelectOnOpen<HTMLInputElement>(editingName)
  const amountInputRef = useFocusSelectOnOpen<HTMLInputElement>(editingAmount)

  // Re-seed the drafts whenever the saved values change. A reducer that replaces
  // the line item in place — another tab's edit landing through a refresh, or a
  // transaction assignment — updates these props without remounting the row, so
  // without this a later-opened editor would show a stale value and could save it
  // back over the newer one. Skip while an editor is open so an in-progress edit
  // is not yanked out from under the user; the draft re-seeds once it closes.
  const [seededName, setSeededName] = useState(lineItem.name)
  if (!editingName && seededName !== lineItem.name) {
    setSeededName(lineItem.name)
    setNameValue(lineItem.name)
  }
  const [seededAmount, setSeededAmount] = useState(lineItem.plannedAmount)
  if (!editingAmount && seededAmount !== lineItem.plannedAmount) {
    setSeededAmount(lineItem.plannedAmount)
    setAmountValue(lineItem.plannedAmount.toString())
  }

  // Re-open the editor if the row is later flagged for editing. Adjusting state
  // during render (rather than in an effect) is React's recommended way to react
  // to a prop change and avoids a cascading re-render.
  const [seededStartEditing, setSeededStartEditing] = useState(startEditing)
  if (seededStartEditing !== startEditing) {
    setSeededStartEditing(startEditing)
    if (startEditing) {
      setEditingName(true)
      setNameValue('')
    }
  }

  const remaining = lineItemRemaining(lineItem)

  const saveUpdate = async (name: string, plannedAmount: number) => {
    if (name === lineItem.name && plannedAmount === lineItem.plannedAmount) {
      return true
    }
    const result = await dispatch(updateLineItem({ lineItemId: lineItem.id, groupId, name, plannedAmount }))
    if (updateLineItem.rejected.match(result)) {
      toast.error(result.payload as string)
      return false
    }
    return true
  }

  const handleSaveName = async () => {
    const trimmed = nameValue.trim()
    if (!trimmed) {
      setNameValue(lineItem.name)
      setEditingName(false)
      onEditComplete?.()
      return
    }
    // A rejected save leaves the prop unchanged, so the re-seed above won't fire —
    // drop the failed draft here or the next blur would silently re-submit it.
    if (!await saveUpdate(trimmed, lineItem.plannedAmount)) {
      setNameValue(lineItem.name)
    }
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
    if (!await saveUpdate(lineItem.name, rounded)) {
      setAmountValue(lineItem.plannedAmount.toString())
    }
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
      className={`grid h-[50px] cursor-pointer items-center gap-4 border-b border-divider px-6 last:border-b-0 hover:bg-[var(--app-hover)] ${
        isSelected ? 'bg-[var(--app-hover)] shadow-[inset_3px_0_0_var(--color-accent)]' : ''
      } ${isOver ? 'bg-accent-100 ring-1 ring-inset ring-accent' : ''}`}
      style={{ gridTemplateColumns: GROUP_GRID_COLUMNS }}
    >
      <div className="flex min-w-0 items-center gap-2">
        {editingName ? (
          <input
            ref={nameInputRef}
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
            className="border border-divider bg-surface px-2 py-0.5 text-sm text-text"
          />
        ) : (
          <span
            onClick={(e) => { e.stopPropagation(); setEditingName(true) }}
            className="cursor-text truncate text-sm text-text hover:text-accent-700"
          >
            {lineItem.name}
          </span>
        )}
        {editingName && <button
          onMouseDown={(e) => e.preventDefault()}
          onClick={(e) => { e.stopPropagation(); handleDelete() }}
          className="shrink-0 text-muted hover:text-accent-700"
          title="Delete item"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-3.5 w-3.5">
            <path fillRule="evenodd" d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.519.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z" clipRule="evenodd" />
          </svg>
        </button>}
      </div>

      <div className="text-right text-sm tabular-nums">
        {editingAmount ? (
          <input
            ref={amountInputRef}
            type="text"
            inputMode="decimal"
            value={amountValue}
            onChange={(e) => { if (isMoneyDraft(e.target.value)) { setAmountValue(e.target.value) } }}
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
            className="w-full border border-divider bg-surface px-2 py-0.5 text-right text-sm text-text"
          />
        ) : (
          <span
            onClick={(e) => { e.stopPropagation(); setEditingAmount(true) }}
            className="cursor-text border border-transparent px-2 py-0.5 text-text hover:[border-bottom-style:dashed] hover:border-b-[var(--color-neutral-500)]"
          >
            {formatCurrency(lineItem.plannedAmount)}
          </span>
        )}
      </div>

      <div className="text-right text-sm tabular-nums">
        {metric === 'actual' ? (
          <FlowAmount
            value={lineItemActual(lineItem)}
            isIncome={isIncome}
            neutralClass="text-text"
            className="px-2 py-0.5"
          />
        ) : (
          <span className={`px-2 py-0.5 ${remaining < 0 ? 'font-semibold text-accent-700' : 'text-text'}`}>
            {formatCurrency(remaining)}
          </span>
        )}
      </div>
    </div>
  )
}
