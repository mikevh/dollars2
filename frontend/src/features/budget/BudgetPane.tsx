import type { BudgetResponse } from '../../types/budget'
import { formatCurrency } from '../../utils/format'
import BudgetGroupCard from './BudgetGroupCard'

interface BudgetPaneProps {
  budget: BudgetResponse
}

export default function BudgetPane({ budget }: BudgetPaneProps) {
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

  return (
    <div>
      <div className={`mb-6 rounded-lg border px-4 py-3 text-center text-sm font-medium ${
        leftToBudget === 0
          ? 'border-green-200 bg-green-50 text-green-700 dark:border-green-800 dark:bg-green-900/20 dark:text-green-400'
          : 'border-red-200 bg-red-50 text-red-700 dark:border-red-800 dark:bg-red-900/20 dark:text-red-400'
      }`}>
        {formatCurrency(totalIncomePlanned)} income − {formatCurrency(totalExpensesPlanned)} expenses = {formatCurrency(leftToBudget)} left to budget
      </div>

      {incomeGroup && <BudgetGroupCard group={incomeGroup} />}
      {expenseGroups.map((group) => (
        <BudgetGroupCard key={group.id} group={group} />
      ))}
    </div>
  )
}
