import { useState } from 'react'
import toast from 'react-hot-toast'
import type { BudgetGroupResponse } from '../../types/budget'
import { useAppDispatch } from '../../app/hooks'
import { updateGroup, deleteGroup, createLineItem } from './budgetSlice'
import LineItemRow from './LineItemRow'

interface BudgetGroupCardProps {
  group: BudgetGroupResponse
  onSelectLineItem?: (lineItemId: number) => void
}

export default function BudgetGroupCard({ group, onSelectLineItem }: BudgetGroupCardProps) {
  const dispatch = useAppDispatch()
  const [editingName, setEditingName] = useState(false)
  const [nameValue, setNameValue] = useState(group.name)
  const [editingNewItemId, setEditingNewItemId] = useState<number | null>(null)

  const columnLabels = group.isIncome
    ? { col1: 'Planned', col2: 'Received', col3: 'Remaining' }
    : { col1: 'Planned', col2: 'Spent', col3: 'Remaining' }

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
    const result = await dispatch(createLineItem({ groupId: group.id, name: 'New Item', plannedAmount: 0 }))
    if (createLineItem.rejected.match(result)) {
      toast.error(result.payload as string)
    } else {
      setEditingNewItemId(result.payload.lineItem.id)
    }
  }

  return (
    <div className="mb-4 overflow-hidden rounded-lg border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
      <div className="flex items-center justify-between bg-gray-50 px-4 py-3 dark:bg-gray-700">
        <div className="flex items-center gap-2">
          {editingName ? (
            <input
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
              autoFocus
              className="rounded border border-gray-300 px-2 py-0.5 text-sm font-semibold uppercase tracking-wide dark:border-gray-500 dark:bg-gray-600 dark:text-white"
            />
          ) : (
            <h3
              onClick={() => {
                if (!group.isIncome) {
                  setEditingName(true)
                }
              }}
              className={`text-sm font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400 ${
                !group.isIncome ? 'cursor-pointer hover:text-gray-700 dark:hover:text-gray-200' : ''
              }`}
            >
              {group.name}
            </h3>
          )}
          {!group.isIncome && editingName && (
            <button
              onMouseDown={(e) => e.preventDefault()}
              onClick={handleDelete}
              className="text-gray-400 hover:text-red-500 dark:text-gray-500 dark:hover:text-red-400"
              title="Delete group"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4">
                <path fillRule="evenodd" d="M8.75 1A2.75 2.75 0 006 3.75v.443c-.795.077-1.584.176-2.365.298a.75.75 0 10.23 1.482l.149-.022.841 10.518A2.75 2.75 0 007.596 19h4.807a2.75 2.75 0 002.742-2.53l.841-10.519.149.023a.75.75 0 00.23-1.482A41.03 41.03 0 0014 4.193V3.75A2.75 2.75 0 0011.25 1h-2.5zM10 4c.84 0 1.673.025 2.5.075V3.75c0-.69-.56-1.25-1.25-1.25h-2.5c-.69 0-1.25.56-1.25 1.25v.325C8.327 4.025 9.16 4 10 4zM8.58 7.72a.75.75 0 00-1.5.06l.3 7.5a.75.75 0 101.5-.06l-.3-7.5zm4.34.06a.75.75 0 10-1.5-.06l-.3 7.5a.75.75 0 101.5.06l.3-7.5z" clipRule="evenodd" />
              </svg>
            </button>
          )}
        </div>
        <div className="flex gap-6 text-xs font-medium uppercase tracking-wide text-gray-400 dark:text-gray-500">
          <span className="w-24 text-right">{columnLabels.col1}</span>
          <span className="w-24 text-right">{columnLabels.col2}</span>
          <span className="w-24 text-right">{columnLabels.col3}</span>
        </div>
      </div>

      {group.lineItems.length === 0 ? (
        <div className="px-4 py-3 text-sm text-gray-400 dark:text-gray-500">
          No items
        </div>
      ) : (
        group.lineItems.map((item) => (
          <LineItemRow
            key={item.id}
            lineItem={item}
            groupId={group.id}
            isIncome={group.isIncome}
            startEditing={item.id === editingNewItemId}
            onEditComplete={() => setEditingNewItemId(null)}
            onSelect={() => onSelectLineItem?.(item.id)}
          />
        ))
      )}

      <div className="border-t border-gray-100 px-4 py-2 dark:border-gray-700">
        <button
          onClick={handleAddItem}
          className="text-sm font-medium text-blue-600 hover:text-blue-700 dark:text-blue-400 dark:hover:text-blue-300"
        >
          + Add Item
        </button>
      </div>
    </div>
  )
}
