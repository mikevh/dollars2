import type { BudgetGroupResponse } from '../../types/budget'
import LineItemRow from './LineItemRow'

interface BudgetGroupCardProps {
  group: BudgetGroupResponse
}

export default function BudgetGroupCard({ group }: BudgetGroupCardProps) {
  const columnLabels = group.isIncome
    ? { col1: 'Planned', col2: 'Received', col3: 'Remaining' }
    : { col1: 'Planned', col2: 'Spent', col3: 'Remaining' }

  return (
    <div className="mb-4 overflow-hidden rounded-lg border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800">
      <div className="flex items-center justify-between bg-gray-50 px-4 py-3 dark:bg-gray-700">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">
          {group.name}
        </h3>
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
          <LineItemRow key={item.id} lineItem={item} isIncome={group.isIncome} />
        ))
      )}
    </div>
  )
}
