import { useState } from 'react'
import toast from 'react-hot-toast'
import type { BudgetResponse } from '../../types/budget'
import { formatCurrency } from '../../utils/format'
import { useAppDispatch } from '../../app/hooks'
import { createGroup } from './budgetSlice'
import BudgetGroupCard from './BudgetGroupCard'

interface BudgetPaneProps {
  budget: BudgetResponse
  onSelectLineItem?: (lineItemId: number) => void
}

export default function BudgetPane({ budget, onSelectLineItem }: BudgetPaneProps) {
  const dispatch = useAppDispatch()
  const [addingGroup, setAddingGroup] = useState(false)
  const [newGroupName, setNewGroupName] = useState('')

  const incomeGroup = budget.groups.find((g) => g.isIncome)
  const expenseGroups = budget.groups.filter((g) => !g.isIncome)

  const totalIncomePlanned = incomeGroup
    ? incomeGroup.lineItems.reduce((sum, item) => sum + item.plannedAmount, 0)
    : 0

  const totalExpensesPlanned = expenseGroups.reduce(
    (sum, group) => sum + group.lineItems.reduce((s, item) => s + item.plannedAmount, 0),
    0
  )

  const leftToBudget = totalIncomePlanned - totalExpensesPlanned

  const handleAddGroup = async () => {
    const name = newGroupName.trim()
    if (!name) {
      return
    }
    const result = await dispatch(createGroup({ budgetId: budget.id, name }))
    if (createGroup.rejected.match(result)) {
      toast.error(result.payload as string)
    } else {
      setNewGroupName('')
      setAddingGroup(false)
    }
  }

  return (
    <div>
      <div className={`mb-6 rounded-lg border px-4 py-3 text-center text-sm font-medium ${
        leftToBudget === 0
          ? 'border-green-200 bg-green-50 text-green-700 dark:border-green-800 dark:bg-green-900/20 dark:text-green-400'
          : 'border-red-200 bg-red-50 text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400'
      }`}>
        {formatCurrency(leftToBudget)} left to budget
      </div>

      {incomeGroup && <BudgetGroupCard group={incomeGroup} onSelectLineItem={onSelectLineItem} />}
      {expenseGroups.map((group) => (
        <BudgetGroupCard key={group.id} group={group} onSelectLineItem={onSelectLineItem} />
      ))}

      {addingGroup ? (
        <div className="mb-4 flex items-center gap-2">
          <input
            type="text"
            value={newGroupName}
            onChange={(e) => setNewGroupName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                handleAddGroup()
              } else if (e.key === 'Escape') {
                setAddingGroup(false)
                setNewGroupName('')
              }
            }}
            placeholder="Group name"
            autoFocus
            className="rounded border border-gray-300 px-3 py-1.5 text-sm dark:border-gray-600 dark:bg-gray-800 dark:text-white"
          />
          <button
            onClick={handleAddGroup}
            className="rounded bg-blue-600 px-3 py-1.5 text-sm text-white hover:bg-blue-700"
          >
            Add
          </button>
          <button
            onClick={() => { setAddingGroup(false); setNewGroupName('') }}
            className="rounded px-3 py-1.5 text-sm text-gray-500 hover:text-gray-700 dark:text-gray-400 dark:hover:text-gray-200"
          >
            Cancel
          </button>
        </div>
      ) : (
        <button
          onClick={() => setAddingGroup(true)}
          className="text-sm font-medium text-blue-600 hover:text-blue-700 dark:text-blue-400 dark:hover:text-blue-300"
        >
          + Add Group
        </button>
      )}
    </div>
  )
}
