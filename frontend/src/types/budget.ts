export interface BudgetResponse {
  id: number
  year: number
  month: number
  accountBalanceTotal: number
  groups: BudgetGroupResponse[]
}

export interface BudgetGroupResponse {
  id: number
  name: string
  sortOrder: number
  lineItems: LineItemResponse[]
}

export interface LineItemResponse {
  id: number
  name: string
  plannedAmount: number
  isIncome: boolean
  spentAmount: number
  receivedAmount: number
  rolloverAmount: number
  sortOrder: number
  notes: string | null
}
