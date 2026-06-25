import { useEffect, useState } from 'react'
import toast from 'react-hot-toast'
import { DndContext, DragOverlay, PointerSensor, useSensor, useSensors } from '@dnd-kit/core'
import type { DragEndEvent, DragStartEvent } from '@dnd-kit/core'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { fetchBudget, createBudget, applyTransactionAssignment } from '../features/budget/budgetSlice'
import { assignTransaction, fetchCounts } from '../features/transactions/transactionSlice'
import MonthNav from '../features/budget/MonthNav'
import BudgetPane from '../features/budget/BudgetPane'
import ActivityPane from '../features/budget/ActivityPane'
import TransactionPane from '../features/transactions/TransactionPane'
import TransactionRow from '../features/transactions/TransactionRow'
import type { TransactionResponse } from '../types/transaction'

export default function BudgetPage() {
  const dispatch = useAppDispatch()
  const { budget, loading, error, currentYear, currentMonth } = useAppSelector(
    (state) => state.budget
  )
  const [draggingTransaction, setDraggingTransaction] = useState<TransactionResponse | null>(null)
  const [selectedLineItemId, setSelectedLineItemId] = useState<number | null>(null)

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } })
  )

  const now = new Date()
  const isPastMonth = currentYear < now.getFullYear() ||
    (currentYear === now.getFullYear() && currentMonth < now.getMonth() + 1)

  useEffect(() => {
    setSelectedLineItemId(null)
    dispatch(fetchBudget({ year: currentYear, month: currentMonth }))
  }, [dispatch, currentYear, currentMonth])

  const handleCreateBudget = async () => {
    const result = await dispatch(createBudget({ year: currentYear, month: currentMonth }))
    if (createBudget.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  const selectedLineItem = (() => {
    if (!budget || !selectedLineItemId) {
      return null
    }
    for (const group of budget.groups) {
      const lineItem = group.lineItems.find((li) => li.id === selectedLineItemId)
      if (lineItem) {
        return { lineItem, isIncome: group.isIncome }
      }
    }
    return null
  })()

  const handleBudgetMutate = () => {
    dispatch(fetchBudget({ year: currentYear, month: currentMonth }))
  }

  const handleDragStart = (event: DragStartEvent) => {
    setDraggingTransaction(event.active.data.current?.transaction ?? null)
  }

  const handleDragEnd = async (event: DragEndEvent) => {
    setDraggingTransaction(null)

    const { active, over } = event
    if (!over) {
      return
    }

    const transaction = active.data.current?.transaction as TransactionResponse
    const lineItemId = over.data.current?.lineItemId as number
    if (!transaction || !lineItemId) {
      return
    }

    const result = await dispatch(assignTransaction({ id: transaction.id, lineItemId }))
    if (assignTransaction.rejected.match(result)) {
      toast.error(result.payload as string)
    } else {
      dispatch(applyTransactionAssignment({ lineItemId, amount: transaction.amount }))
      dispatch(fetchCounts())
    }
  }

  return (
    <DndContext sensors={sensors} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
      <div className="min-h-screen bg-gray-50 pb-16 dark:bg-gray-900" onClick={() => setSelectedLineItemId(null)}>
        <div className="mx-auto max-w-6xl px-4">
          <MonthNav />

          <div className="flex gap-6">
            <div className="flex-1">
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

              {!loading && budget && <BudgetPane budget={budget} onSelectLineItem={setSelectedLineItemId} />}
            </div>

            <div className="w-96 rounded-lg border border-gray-200 bg-white dark:border-gray-700 dark:bg-gray-800" onClick={(e) => e.stopPropagation()}>
              {selectedLineItem ? (
                <ActivityPane
                  lineItem={selectedLineItem.lineItem}
                  isIncome={selectedLineItem.isIncome}
                  budgetMonth={currentMonth}
                  onClose={() => setSelectedLineItemId(null)}
                  onBudgetMutate={handleBudgetMutate}
                />
              ) : (
                <TransactionPane onBudgetMutate={handleBudgetMutate} />
              )}
            </div>
          </div>
        </div>
      </div>

      <DragOverlay dropAnimation={null}>
        {draggingTransaction && (
          <div className="w-96 rounded-lg border border-blue-300 bg-white shadow-lg dark:border-blue-600 dark:bg-gray-800">
            <TransactionRow transaction={draggingTransaction} />
          </div>
        )}
      </DragOverlay>
    </DndContext>
  )
}
