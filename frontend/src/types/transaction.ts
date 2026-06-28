export interface TransactionResponse {
  id: number
  accountId: number | null
  accountName: string | null
  date: string
  description: string
  payee: string
  memo: string
  amount: number
  notes: string | null
  isDeleted: boolean
  isPending: boolean
  isManual: boolean
  assignments: TransactionAssignmentResponse[]
}

export interface TransactionAssignmentResponse {
  id: number
  lineItemId: number
  lineItemName: string
  amount: number
}
