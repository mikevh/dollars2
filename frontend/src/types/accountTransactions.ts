import type { TransactionResponse } from './transaction'

export interface AccountTransactions {
  accountId: number
  accountName: string
  transactions: TransactionResponse[]
  totalCount: number
}
