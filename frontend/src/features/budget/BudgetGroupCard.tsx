import { useEffect, useRef, useState } from 'react'
import toast from 'react-hot-toast'
import type { BudgetGroupResponse } from '../../types/budget'
import { formatCurrency } from '../../utils/format'
import { useAppDispatch } from '../../app/hooks'
import { useFocusSelectOnOpen } from '../../hooks/useFocusSelectOnOpen'
import { updateGroup, deleteGroup, createLineItem } from './budgetSlice'
import { GROUP_GRID_COLUMNS, type GroupMetric } from './groupGridColumns'
import LineItemRow from './LineItemRow'

interface BudgetGroupCardProps {
  group: BudgetGroupResponse
  selectedLineItemId?: number | null
  onSelectLineItem?: (lineItemId: number) => void
}

function ChevronIcon({ className = 'h-4 w-4' }: { className?: string }) {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className={className}>
      <path fillRule="evenodd" d="M5.22 8.22a.75.75 0 011.06 0L10 11.94l3.72-3.72a.75.75 0 111.06 1.06l-4.25 4.25a.75.75 0 01-1.06 0L5.22 9.28a.75.75 0 010-1.06z" clipRule="evenodd" />
    </svg>
  )
}

function CheckIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4 text-[var(--color-accent)]">
      <path fillRule="evenodd" d="M16.704 4.153a.75.75 0 01.143 1.052l-8 10.5a.75.75 0 01-1.127.075l-4.5-4.5a.75.75 0 011.06-1.06l3.894 3.893 7.48-9.817a.75.75 0 011.05-.143z" clipRule="evenodd" />
    </svg>
  )
}

export default function BudgetGroupCard({ group, selectedLineItemId, onSelectLineItem }: BudgetGroupCardProps) {
  const dispatch = useAppDispatch()
  const [editingName, setEditingName] = useState(false)
  const [nameValue, setNameValue] = useState(group.name)
  const [editingNewItemId, setEditingNewItemId] = useState<number | null>(null)
  const [collapsed, setCollapsed] = useState(false)
  const [metric, setMetric] = useState<GroupMetric>('remaining')
  const [metricMenuOpen, setMetricMenuOpen] = useState(false)
  const metricMenuRef = useRef<HTMLDivElement>(null)
  const nameInputRef = useFocusSelectOnOpen<HTMLInputElement>(editingName)

  // Close the metric menu on any outside mousedown, per the redesign's dropdown spec.
  useEffect(() => {
    if (!metricMenuOpen) {
      return
    }
    const handleMouseDown = (e: MouseEvent) => {
      if (metricMenuRef.current && !metricMenuRef.current.contains(e.target as Node)) {
        setMetricMenuOpen(false)
      }
    }
    document.addEventListener('mousedown', handleMouseDown)
    return () => document.removeEventListener('mousedown', handleMouseDown)
  }, [metricMenuOpen])

  // Re-seed the draft whenever the saved name changes. A reducer that replaces the
  // group in place updates this prop without remounting the card, so without this
  // a later-opened editor would show a stale name and could save it back over the
  // newer one. Skip while the editor is open so an in-progress edit is not yanked
  // out from under the user; the draft re-seeds once it closes.
  const [seededName, setSeededName] = useState(group.name)
  if (!editingName && seededName !== group.name) {
    setSeededName(group.name)
    setNameValue(group.name)
  }

  const isAllIncome = group.lineItems.length > 0 && group.lineItems.every((item) => item.isIncome)
  const actualLabel = isAllIncome ? 'Received' : 'Spent'
  const metricLabel = metric === 'remaining' ? 'Remaining' : actualLabel

  const totalPlanned = group.lineItems.reduce((sum, item) => sum + item.plannedAmount, 0)
  const totalActual = group.lineItems.reduce(
    (sum, item) => sum + (item.isIncome ? item.receivedAmount : item.spentAmount),
    0,
  )
  const totalRemaining = group.lineItems.reduce(
    (sum, item) =>
      sum + (item.isIncome ? item.plannedAmount - item.receivedAmount : item.plannedAmount + item.rolloverAmount - item.spentAmount),
    0,
  )
  const totalMetric = metric === 'remaining' ? totalRemaining : totalActual

  const handleSaveName = async () => {
    const trimmed = nameValue.trim()
    if (!trimmed || trimmed === group.name) {
      setEditingName(false)
      setNameValue(group.name)
      return
    }
    const result = await dispatch(updateGroup({ groupId: group.id, name: trimmed }))
    if (updateGroup.rejected.match(result)) {
      toast.error(result.payload as string)
      setNameValue(group.name)
    }
    setEditingName(false)
  }

  const handleDelete = async () => {
    const result = await dispatch(deleteGroup({ groupId: group.id }))
    if (deleteGroup.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  const handleAddItem = async () => {
    const result = await dispatch(createLineItem({ groupId: group.id, name: 'New Item', plannedAmount: 0, isIncome: isAllIncome }))
    if (createLineItem.rejected.match(result)) {
      toast.error(result.payload as string)
    } else {
      setEditingNewItemId(result.payload.lineItem.id)
    }
  }

  return (
    <div className="mb-8">
      <div className="card">
        <div className="border-b-2 border-divider px-6 pt-[22px] pb-[18px]">
          <div className="grid items-center gap-4" style={{ gridTemplateColumns: GROUP_GRID_COLUMNS }}>
            <div className="flex min-w-0 items-center gap-2">
              <button
                onClick={() => setCollapsed((c) => !c)}
                aria-label={collapsed ? 'Expand group' : 'Collapse group'}
                className="text-muted hover:text-text"
              >
                <ChevronIcon className={`h-4 w-4 transition-transform ${collapsed ? '-rotate-90' : ''}`} />
              </button>
              {editingName ? (
                <input
                  ref={nameInputRef}
                  type="text"
                  value={nameValue}
                  onChange={(e) => setNameValue(e.target.value)}
                  onBlur={handleSaveName}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      handleSaveName()
                    } else if (e.key === 'Escape') {
                      setEditingName(false)
                      setNameValue(group.name)
                    }
                  }}
                  className="input max-w-[240px] font-heading text-sm font-extrabold uppercase tracking-wide"
                />
              ) : (
                <h3
                  onClick={() => setEditingName(true)}
                  className="cursor-pointer truncate font-heading text-sm font-extrabold uppercase tracking-wide text-text hover:text-accent-700"
                >
                  {group.name}
                </h3>
              )}
              {editingName && (
                <button
                  onMouseDown={(e) => e.preventDefault()}
                  onClick={handleDelete}
                  className="shrink-0 text-muted hover:text-accent-700"
                  title="Delete group"
                >
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4">
                    <path fillRule="evenodd" d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.519.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z" clipRule="evenodd" />
                  </svg>
                </button>
              )}
            </div>

            <div className="flex justify-end pr-4">
              <span className="text-right font-heading text-[11px] font-bold uppercase tracking-[0.08em] text-muted">
                Planned
              </span>
            </div>

            <div ref={metricMenuRef} className="relative">
              <button
                onClick={() => setMetricMenuOpen((o) => !o)}
                className="flex w-full items-center justify-end gap-1 font-heading text-[11px] font-bold uppercase tracking-[0.08em] text-muted hover:text-text"
              >
                {metricLabel}
                <ChevronIcon className="h-3.5 w-3.5" />
              </button>
              {metricMenuOpen && (
                <div
                  className="absolute right-0 top-full z-10 mt-1 w-36 rounded-[var(--radius-control)] bg-[var(--app-card)] py-1.5 shadow-[var(--app-shadow-lg)]"
                >
                  {(['remaining', 'actual'] as GroupMetric[]).map((m) => (
                    <button
                      key={m}
                      onClick={() => { setMetric(m); setMetricMenuOpen(false) }}
                      className="flex h-11 w-full items-center justify-between px-3 text-sm normal-case tracking-normal text-text hover:bg-[var(--app-hover)]"
                    >
                      <span>{m === 'remaining' ? 'Remaining' : actualLabel}</span>
                      {metric === m && <CheckIcon />}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        </div>

        {collapsed ? (
          <div className="flex h-[60px] items-center justify-between px-6 text-sm text-muted">
            <span>{group.lineItems.length} items</span>
            <div className="flex gap-6 font-heading text-sm font-extrabold text-text">
              <span className="w-24 text-right">{formatCurrency(totalPlanned)}</span>
              <span className="w-24 text-right">{formatCurrency(totalMetric)}</span>
            </div>
          </div>
        ) : group.lineItems.length === 0 ? (
          <div className="px-6 py-3 text-sm text-muted">
            No items
          </div>
        ) : (
          group.lineItems.map((item) => (
            <LineItemRow
              key={item.id}
              lineItem={item}
              groupId={group.id}
              metric={metric}
              isSelected={item.id === selectedLineItemId}
              startEditing={item.id === editingNewItemId}
              onEditComplete={() => setEditingNewItemId(null)}
              onSelect={() => onSelectLineItem?.(item.id)}
            />
          ))
        )}

        <div className="flex h-[68px] items-center justify-between border-t border-divider px-6">
          <button
            onClick={handleAddItem}
            className="font-heading text-sm font-extrabold text-[var(--app-blue)] hover:text-[var(--app-blue-hover)]"
          >
            {isAllIncome ? '+ Add income' : '+ Add item'}
          </button>
          <div className="flex gap-6 font-heading text-[20px] font-extrabold text-text">
            <span className="w-24 text-right">{formatCurrency(totalPlanned)}</span>
            <span className="w-24 text-right">{formatCurrency(totalMetric)}</span>
          </div>
        </div>
      </div>
    </div>
  )
}
