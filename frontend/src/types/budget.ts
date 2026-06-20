export interface BudgetResponse {
  id: number
  year: number
  month: number
  groups: BudgetGroupResponse[]
}

export interface BudgetGroupResponse {
  id: number
  name: string
  isIncome: boolean
  sortOrder: number
  lineItems: LineItemResponse[]
}

export interface LineItemResponse {
  id: number
  name: string
  plannedAmount: number
  spentAmount: number
  receivedAmount: number
  sortOrder: number
  notes: string | null
}
