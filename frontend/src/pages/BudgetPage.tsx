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

export default function BudgetPage() 
{
  const dispatch = useAppDispatch();
  const { 
    budget, 
    loading, 
    error, 
    currentYear, 
    currentMonth
  } = useAppSelector(s => s.budget);
  
  const [draggingTransaction, setDraggingTransaction] = useState<TransactionResponse | null>(null);
  const [selectedLineItemId, setSelectedLineItemId] = useState<number | null>(null);

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }));
  const now = new Date();
  const isPastMonth = currentYear < now.getFullYear() ||
    (currentYear === now.getFullYear() && currentMonth < now.getMonth() + 1);

  // Drop the activity selection when the month changes — the line item belongs to
  // the month being navigated away from. Adjusting state during render (rather than
  // in an effect) is React's recommended way to reset state on an input change and
  // avoids a cascading re-render.
  const monthKey = `${currentYear}-${currentMonth}`;
  const [selectedMonthKey, setSelectedMonthKey] = useState(monthKey);
  if (selectedMonthKey !== monthKey) {
    setSelectedMonthKey(monthKey);
    setSelectedLineItemId(null);
  }

  useEffect(() => {
    dispatch(fetchBudget({ year: currentYear, month: currentMonth }));
  }, [dispatch, currentYear, currentMonth]);

  const handleCreateBudget = async () => {
    const result = await dispatch(createBudget({ year: currentYear, month: currentMonth }));
    if (createBudget.rejected.match(result)) {
      toast.error(result.payload as string);
    }
  };

  // A fetch in flight (or a rejected fetch) leaves the previous month's budget in the
  // store rather than nulling it out, so the pane can stay mounted during a same-month
  // refresh. Gate rendering on the budget actually matching the month being viewed —
  // otherwise a month navigation would flash the month being left behind.
  const currentMonthBudget = budget && budget.year === currentYear && 
    budget.month === currentMonth ? budget : null;

  const selectedLineItem = (() => {
    if (!currentMonthBudget || !selectedLineItemId) {
      return null;
    }

    for (const group of currentMonthBudget.groups) {
      const lineItem = group.lineItems.find(li => li.id === selectedLineItemId);
      if (lineItem) {
        return { lineItem, isIncome: lineItem.isIncome };
      }
    }

    return null;
  })();

  const handleBudgetMutate = async () => {
    const result = await dispatch(fetchBudget({ year: currentYear, month: currentMonth }))
    if (fetchBudget.rejected.match(result)) {
      toast.error(result.payload as string)
    }
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
      const confirmed = window.confirm('This transaction is from a different month than the budget you are viewing. Are you sure you want to assign it?');
      if (!confirmed) {
        return;
      }
    }

    await doAssign(transaction, lineItemId)
  }

  return (
    <DndContext sensors={sensors} onDragStart={handleDragStart} onDragEnd={handleDragEnd}>
      <div className="flex min-h-screen flex-col bg-bg pb-14 text-text" onClick={() => setSelectedLineItemId(null)}>
        <MonthNav />

        <div className="mx-auto flex w-full max-w-295 items-start gap-6 px-4 py-6">
          <div className="min-w-0 flex-1">
            {loading && !currentMonthBudget && (
              <div className="text-muted py-12 text-center">Loading...</div>
            )}

            {!loading && !currentMonthBudget && error === 'BUDGET_NOT_FOUND' && (
              <div className="py-12 text-center">
                <p className="text-muted mb-4">No budget for this month.</p>
                {!isPastMonth && (
                  <button onClick={handleCreateBudget} className="btn btn-primary">Create Budget</button>
                )}
              </div>
            )}

            {!loading && 
              !currentMonthBudget && 
              error && 
              error !== 'BUDGET_NOT_FOUND' && (
                <div className="py-12 text-center text-accent-700">{error}</div>
            )}

            {currentMonthBudget && (
              <div className={loading ? 'opacity-60' : ''} aria-busy={loading}>
                <BudgetPane budget={currentMonthBudget} onSelectLineItem={setSelectedLineItemId} />
              </div>
            )}
          </div>

          {/* Pinned so the drag source stays on screen while the budget list scrolls past it.
              top-86px = the 62px sticky MonthNav + this row's py-6, i.e. exactly where the pane
              already sits unscrolled, so it never jumps. Height is bounded to the viewport
              (86px offset + 56px footer reserve + 8px gap) with no min-height: a sticky box
              taller than its slot pins at `top` and its overflowing bottom becomes unreachable.
              TransactionPane scrolls its own list, so fitting the viewport costs nothing. */}
          <div
            className="sticky top-21.5 flex h-[calc(100vh-150px)] w-95 flex-none flex-col border border-divider bg-surface shadow-elev-sm"
            onClick={e => e.stopPropagation()}
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

      <DragOverlay dropAnimation={null}>
        {draggingTransaction && (
          <div className="w-95 border border-divider bg-surface shadow-elev-lg">
            <TransactionRow transaction={draggingTransaction} />
          </div>
        )}
      </DragOverlay>
    </DndContext>
  )
}
