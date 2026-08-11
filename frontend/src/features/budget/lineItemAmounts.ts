import type { LineItemResponse } from '../../types/budget'

/** Planned + rollover − spent (expense) or planned − received (income). */
export function lineItemRemaining(item: LineItemResponse): number {
  return item.isIncome
    ? item.plannedAmount - item.receivedAmount
    : item.plannedAmount + item.rolloverAmount - item.spentAmount
}

/** Received (income) or spent (expense) — the "actual" money-column metric. */
export function lineItemActual(item: LineItemResponse): number {
  return item.isIncome ? item.receivedAmount : item.spentAmount
}
