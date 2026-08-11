import { useState } from 'react'
import toast from 'react-hot-toast'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import type { BudgetResponse } from '../../types/budget'
import { formatCurrency } from '../../utils/format'
import { useAppDispatch } from '../../app/hooks'
import { createGroup } from './budgetSlice'
import BudgetGroupCard from './BudgetGroupCard'

interface BudgetPaneProps {
  budget: BudgetResponse
  selectedLineItemId?: number | null
  onSelectLineItem?: (lineItemId: number) => void
}

export default function BudgetPane({ budget, selectedLineItemId, onSelectLineItem }: BudgetPaneProps) {
  const dispatch = useAppDispatch()
  const [addingGroup, setAddingGroup] = useState(false)
  const [newGroupName, setNewGroupName] = useState('')

  const allLineItems = budget.groups.flatMap((group) => group.lineItems)

  const totalIncomePlanned = allLineItems
    .filter((item) => item.isIncome)
    .reduce((sum, item) => sum + item.plannedAmount, 0)

  const totalExpensesPlanned = allLineItems
    .filter((item) => !item.isIncome)
    .reduce((sum, item) => sum + item.plannedAmount, 0)

  const leftToBudget = totalIncomePlanned - totalExpensesPlanned

  // Income line items contribute planned only; expenses contribute planned + rollover - spent.
  // The income branch is load-bearing: spentAmount is the negated net of a line item's assignments,
  // so on an income item it equals -receivedAmount, and a sign-blind loop would add already-received
  // income on top of planned. See issue #73 for whether this total should net out received income.
  const budgetTotal = allLineItems.reduce(
    (sum, item) =>
      sum +
      (item.isIncome
        ? item.plannedAmount
        : item.plannedAmount + item.rolloverAmount - item.spentAmount),
    0
  )
  const budgetVsAccounts = budget.accountBalanceTotal - budgetTotal

  const now = new Date()
  const isCurrentMonth =
    budget.year === now.getFullYear() && budget.month === now.getMonth() + 1

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
    <>
      <div className="card mb-6 flex items-baseline px-6 py-3">
        <span className="font-heading text-[14px] font-bold uppercase tracking-[0.09em] text-neutral-700">
          Left
        </span>
        <span
          className={`ml-2 font-heading text-[24px] font-bold tabular-nums ${
            leftToBudget === 0 ? 'text-text' : 'text-accent-700'
          }`}
        >
          {formatCurrency(leftToBudget)}
        </span>
        {isCurrentMonth && (
          <>
            <span className="ml-auto font-heading text-[14px] font-bold uppercase tracking-[0.09em] text-neutral-700">
              Delta
            </span>
            <span
              className={`ml-2 font-heading text-[24px] font-bold tabular-nums ${
                budgetVsAccounts === 0 ? 'text-text' : 'text-accent-700'
              }`}
            >
              {formatCurrency(budgetVsAccounts)}
            </span>
          </>
        )}
      </div>

      <div>
        <SortableContext items={budget.groups.map((group) => group.id)} strategy={verticalListSortingStrategy}>
          {budget.groups.map((group) => (
            <BudgetGroupCard
              key={group.id}
              group={group}
              selectedLineItemId={selectedLineItemId}
              onSelectLineItem={onSelectLineItem}
            />
          ))}
        </SortableContext>

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
          <button
            onClick={() => setAddingGroup(true)}
            className="font-heading text-sm font-extrabold text-[var(--app-blue)] hover:text-[var(--app-blue-hover)]"
          >
            + Add Group
          </button>
        )}
      </div>
    </>
  )
}
