import { useEffect } from 'react'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { fetchBudget, createBudget } from '../features/budget/budgetSlice'
import MonthNav from '../features/budget/MonthNav'
import BudgetPane from '../features/budget/BudgetPane'

export default function BudgetPage() {
  const dispatch = useAppDispatch()
  const { budget, loading, error, currentYear, currentMonth } = useAppSelector(
    (state) => state.budget
  )

  const now = new Date()
  const isPastMonth = currentYear < now.getFullYear() ||
    (currentYear === now.getFullYear() && currentMonth < now.getMonth() + 1)

  useEffect(() => {
    dispatch(fetchBudget({ year: currentYear, month: currentMonth }))
  }, [dispatch, currentYear, currentMonth])

  const handleCreateBudget = async () => {
    const result = await dispatch(createBudget({ year: currentYear, month: currentMonth }))
    if (createBudget.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  return (
    <div className="min-h-screen bg-gray-50 pb-16 dark:bg-gray-900">
      <div className="mx-auto max-w-2xl px-4">
        <MonthNav />

        {loading && (
          <div className="py-12 text-center text-gray-500 dark:text-gray-400">Loading...</div>
        )}

        {!loading && error === 'BUDGET_NOT_FOUND' && (
          <div className="py-12 text-center">
            <p className="mb-4 text-gray-500 dark:text-gray-400">
              No budget for this month.
            </p>
            {!isPastMonth && (
              <button
                onClick={handleCreateBudget}
                className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
              >
                Create Budget
              </button>
            )}
          </div>
        )}

        {!loading && error && error !== 'BUDGET_NOT_FOUND' && (
          <div className="py-12 text-center text-red-500">{error}</div>
        )}

        {!loading && budget && <BudgetPane budget={budget} />}
      </div>
    </div>
  )
}
