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

/** One archived sighting of a transaction, as the provider sent it during a sync run. */
export interface RawHistoryEntryResponse {
  syncedAt: string
  sourceType: string
  syncRunId: string
  /** The provider payload verbatim — a JSON string the server never re-parses. */
  rawJson: string
}
