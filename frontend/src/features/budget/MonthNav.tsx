import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faChevronLeft, faChevronRight } from '@fortawesome/free-solid-svg-icons'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import { nextMonth, prevMonth } from './budgetSlice'

const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June',
  'July', 'August', 'September', 'October', 'November', 'December',
]

export default function MonthNav() {
  const dispatch = useAppDispatch()
  const { currentYear, currentMonth } = useAppSelector((state) => state.budget)

  return (
    <div className="flex items-center justify-center gap-4 py-4">
      <button
        onClick={() => dispatch(prevMonth())}
        className="rounded p-2 text-gray-600 hover:text-gray-900 dark:text-gray-400 dark:hover:text-white"
      >
        <FontAwesomeIcon icon={faChevronLeft} />
      </button>
      <h2 className="w-48 text-center text-lg font-semibold text-gray-900 dark:text-white">
        {MONTH_NAMES[currentMonth - 1]} {currentYear}
      </h2>
      <button
        onClick={() => dispatch(nextMonth())}
        className="rounded p-2 text-gray-600 hover:text-gray-900 dark:text-gray-400 dark:hover:text-white"
      >
        <FontAwesomeIcon icon={faChevronRight} />
      </button>
    </div>
  )
}
