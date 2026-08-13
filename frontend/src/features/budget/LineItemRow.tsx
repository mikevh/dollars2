import { useState } from 'react'
import toast from 'react-hot-toast'
import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
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
  wizardStage?: 'name' | 'amount' | null
  onWizardAdvance?: () => void
  onWizardComplete?: () => void
  onSelect?: () => void
}

export default function LineItemRow({ lineItem, groupId, metric, isSelected, wizardStage, onWizardAdvance, onWizardComplete, onSelect }: LineItemRowProps) {
  const isIncome = lineItem.isIncome
  const dispatch = useAppDispatch()
  // A freshly added item mounts with wizardStage 'name', so seed the name editor open
  // and empty rather than opening it from an effect after the first paint.
  const [editingName, setEditingName] = useState(wizardStage === 'name')
  const [editingAmount, setEditingAmount] = useState(false)
  const editing = editingName || editingAmount
  // Same node doubles as the transaction-assignment drop target (isOver below) and the
  // reorder drag source — dnd-kit hooks bind to the nearest DndContext, so this has to
  // stay one registration under the page-level context rather than a separate droppable.
  // Disabled while editing so a click-drag used to select input text can't be read as a
  // reorder gesture. Only `listeners` (not `attributes`) are spread onto the row below —
  // `attributes` defaults `role="button"`/`tabIndex`, which nests a fake button around the
  // row's real name/delete buttons; dropping it costs nothing since only PointerSensor is
  // wired up (no KeyboardSensor for that role/tabIndex to serve).
  const {
    listeners,
    setNodeRef,
    transform,
    transition,
    isOver,
    isDragging,
    active,
  } = useSortable({
    id: `lineitem-${lineItem.id}`,
    data: { type: 'lineitem', lineItemId: lineItem.id, groupId },
    disabled: editing,
  })
  // Geometry alone (isOver) lights up for any drag hovering the row, including a line item
  // from a different group or a group card — drops the row's handlers will silently ignore.
  // Only show the drop-target affordance for drags this row will actually accept: any
  // transaction, or a line item from the same group.
  const activeData = active?.data.current
  const isValidDropHover = isOver && (
    activeData?.type === 'transaction' ||
    (activeData?.type === 'lineitem' && activeData?.groupId === groupId)
  )
  const [nameValue, setNameValue] = useState(wizardStage === 'name' ? '' : lineItem.name)
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
  const [seededWizardStage, setSeededWizardStage] = useState(wizardStage ?? null)
  if (seededWizardStage !== (wizardStage ?? null)) {
    setSeededWizardStage(wizardStage ?? null)
    if (wizardStage === 'name') {
      setEditingName(true)
      setNameValue('')
    } else if (wizardStage == null) {
      // The parent reassigns the wizard to a different row (e.g. + Add Item clicked
      // again) without waiting for this row's rename to resolve — close out whatever draft
      // was left open here rather than leaving an orphaned editor with no owner.
      setEditingName(false)
      setNameValue(lineItem.name)
    }
    // wizardStage === 'amount' needs no action here — handleSaveName already opened the
    // amount editor locally before telling the parent the wizard advanced.
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
      onWizardComplete?.()
      return
    }
    if (!await saveUpdate(trimmed, lineItem.plannedAmount)) {
      if (wizardStage === 'name') {
        // Leave the rejected draft in the input rather than reverting it to lineItem.name —
        // reverting would make an unmodified next blur match lineItem.name and pass through
        // saveUpdate's unchanged-value short-circuit, silently "succeeding" without ever
        // retrying the save. Refocus since the blur that got us here already moved focus away.
        nameInputRef.current?.focus()
        nameInputRef.current?.select()
        return
      }
      // A rejected save leaves the prop unchanged, so the re-seed above won't fire —
      // drop the failed draft here or the next blur would silently re-submit it.
      setNameValue(lineItem.name)
    }
    setEditingName(false)
    if (wizardStage === 'name') {
      setEditingAmount(true)
      onWizardAdvance?.()
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
    if (wizardStage === 'amount') {
      onWizardComplete?.()
    }
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
      {...(editing ? {} : listeners)}
      onClick={(e) => { e.stopPropagation(); onSelect?.() }}
      className={`grid h-[50px] items-center gap-4 border-b border-divider px-6 last:border-b-0 hover:bg-[var(--app-hover)] ${
        editing ? 'cursor-pointer' : 'cursor-grab active:cursor-grabbing'
      } ${isSelected ? 'bg-[var(--app-hover)] shadow-[inset_3px_0_0_var(--color-accent)]' : ''} ${
        isValidDropHover ? 'bg-accent-100 ring-1 ring-inset ring-accent' : ''
      } ${isDragging ? 'opacity-50' : ''}`}
      style={{ gridTemplateColumns: GROUP_GRID_COLUMNS, transform: CSS.Transform.toString(transform), transition }}
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
