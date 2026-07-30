import { useEffect, useId, useState } from 'react'
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
import Dialog from '../components/Dialog'
import type { TransactionResponse } from '../types/transaction'

export default function BudgetPage() {
  const dispatch = useAppDispatch()
  const { budget, loading, error, currentYear, currentMonth } = useAppSelector(
    (state) => state.budget
  )
  const [draggingTransaction, setDraggingTransaction] = useState<TransactionResponse | null>(null)
  const [selectedLineItemId, setSelectedLineItemId] = useState<number | null>(null)
  const [crossMonthPending, setCrossMonthPending] = useState<{ transaction: TransactionResponse; lineItemId: number } | null>(null)
  const crossMonthTitleId = useId()

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 5 } })
  )

  const now = new Date()
  const isPastMonth = currentYear < now.getFullYear() ||
    (currentYear === now.getFullYear() && currentMonth < now.getMonth() + 1)

  // Drop the activity selection when the month changes — the line item belongs to
  // the month being navigated away from. Adjusting state during render (rather than
  // in an effect) is React's recommended way to reset state on an input change and
  // avoids a cascading re-render.
  const monthKey = `${currentYear}-${currentMonth}`
  const [selectedMonthKey, setSelectedMonthKey] = useState(monthKey)
  if (selectedMonthKey !== monthKey) {
    setSelectedMonthKey(monthKey)
    setSelectedLineItemId(null)
  }

  useEffect(() => {
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

  const doAssign = async (transaction: TransactionResponse, lineItemId: number) => {
    const result = await dispatch(assignTransaction({ id: transaction.id, lineItemId }))
    if (assignTransaction.rejected.match(result)) {
      toast.error(result.payload as string)
    } else {
      dispatch(applyTransactionAssignment({ lineItemId, amount: transaction.amount }))
      dispatch(fetchCounts())
    }
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

    const txDate = new Date(transaction.date.slice(0, 10) + 'T00:00:00')
    const isCrossMonth = txDate.getFullYear() !== currentYear || txDate.getMonth() + 1 !== currentMonth
    if (isCrossMonth) {
      setCrossMonthPending({ transaction, lineItemId })
      return
    }

    await doAssign(transaction, lineItemId)
  }

  return (
    <DndContext sensors={sensors} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
      <div className="flex min-h-screen flex-col bg-bg pb-14 text-text" onClick={() => setSelectedLineItemId(null)}>
        <MonthNav />

        <div className="mx-auto flex w-full max-w-[1180px] items-start gap-6 px-4 py-6">
          <div className="min-w-0 flex-1">
            {loading && (
              <div className="text-muted py-12 text-center">Loading...</div>
            )}

            {!loading && error === 'BUDGET_NOT_FOUND' && (
              <div className="py-12 text-center">
                <p className="text-muted mb-4">
                  No budget for this month.
                </p>
                {!isPastMonth && (
                  <button
                    onClick={handleCreateBudget}
                    className="btn btn-primary"
                  >
                    Create Budget
                  </button>
                )}
              </div>
            )}

            {!loading && error && error !== 'BUDGET_NOT_FOUND' && (
              <div className="py-12 text-center text-accent-700">{error}</div>
            )}

            {!loading && budget && <BudgetPane budget={budget} onSelectLineItem={setSelectedLineItemId} />}
          </div>

          {/* Pinned so the drag source stays on screen while the budget list scrolls past it.
              top-86px = the 62px sticky MonthNav + this row's py-6, i.e. exactly where the pane
              already sits unscrolled, so it never jumps. Height is bounded to the viewport
              (86px offset + 56px footer reserve + 8px gap) with no min-height: a sticky box
              taller than its slot pins at `top` and its overflowing bottom becomes unreachable.
              TransactionPane scrolls its own list, so fitting the viewport costs nothing. */}
          <div
            className="sticky top-[86px] flex h-[calc(100vh-150px)] w-[380px] flex-none flex-col border border-divider bg-surface shadow-elev-sm"
            onClick={(e) => e.stopPropagation()}
          >
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

      {crossMonthPending && (() => {
        const txDate = new Date(crossMonthPending.transaction.date.slice(0, 10) + 'T00:00:00')
        const txMonthName = txDate.toLocaleString('default', { month: 'long' })
        const budgetMonthName = new Date(currentYear, currentMonth - 1).toLocaleString('default', { month: 'long' })
        return (
          <Dialog
            onClose={() => setCrossMonthPending(null)}
            labelledBy={crossMonthTitleId}
            className="max-w-[420px]"
          >
            <h2 id={crossMonthTitleId} className="mb-2 text-[18px]">Cross-month assignment</h2>
            <p className="text-muted mb-5 text-[14px]">
              This transaction is from <strong className="text-text">{txMonthName}</strong> but you're viewing <strong className="text-text">{budgetMonthName}</strong>. Assign it anyway?
            </p>
            <div className="flex justify-end gap-2">
              <button
                onClick={() => setCrossMonthPending(null)}
                className="btn btn-secondary"
              >
                Cancel
              </button>
              <button
                onClick={async () => {
                  const { transaction, lineItemId } = crossMonthPending
                  setCrossMonthPending(null)
                  await doAssign(transaction, lineItemId)
                }}
                className="btn btn-primary"
              >
                Assign anyway
              </button>
            </div>
          </Dialog>
        )
      })()}

      <DragOverlay dropAnimation={null}>
        {draggingTransaction && (
          <div className="w-[380px] border border-divider bg-surface shadow-elev-lg">
            <TransactionRow transaction={draggingTransaction} />
          </div>
        )}
      </DragOverlay>
    </DndContext>
  )
}
