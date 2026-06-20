import type { LineItemResponse } from '../../types/budget'
import { formatCurrency } from '../../utils/format'

interface LineItemRowProps {
  lineItem: LineItemResponse
  isIncome: boolean
}

export default function LineItemRow({ lineItem, isIncome }: LineItemRowProps) {
  const received = 0
  const spent = 0
  const remaining = isIncome
    ? lineItem.plannedAmount - received
    : lineItem.plannedAmount - spent

  return (
    <div className="flex items-center justify-between border-b border-gray-100 px-4 py-2 last:border-b-0 dark:border-gray-700">
      <span className="text-sm text-gray-700 dark:text-gray-300">{lineItem.name}</span>
      <div className="flex gap-6 text-sm">
        <span className="w-24 text-right text-gray-900 dark:text-white">
          {formatCurrency(lineItem.plannedAmount)}
        </span>
        <span className="w-24 text-right text-gray-500 dark:text-gray-400">
          {formatCurrency(isIncome ? received : spent)}
        </span>
        <span className={`w-24 text-right ${remaining < 0 ? 'text-red-500' : 'text-gray-900 dark:text-white'}`}>
          {formatCurrency(remaining)}
        </span>
      </div>
    </div>
  )
}
