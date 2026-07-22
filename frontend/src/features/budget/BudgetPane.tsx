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
      <div className="mb-6 flex items-baseline justify-between border-b-2 border-divider pb-4">
        <span className="font-heading text-[13px] font-extrabold uppercase tracking-[0.08em] text-muted">
          Left to budget
        </span>
        <span
          className={`font-heading text-[22px] font-extrabold tabular-nums ${
            leftToBudget === 0 ? 'text-text' : 'text-accent-700'
          }`}
        >
          {formatCurrency(leftToBudget)}
        </span>
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
            className="input max-w-[240px]"
          />
          <button onClick={handleAddGroup} className="btn btn-primary">
            Add
          </button>
          <button
            onClick={() => { setAddingGroup(false); setNewGroupName('') }}
            className="btn btn-secondary"
          >
            Cancel
          </button>
        </div>
      ) : (
        <button onClick={() => setAddingGroup(true)} className="btn btn-ghost">
          + Add Group
        </button>
      )}
    </div>
  )
}
