export interface AccountInfo {
  id: number
  name: string
  lastSyncedAt: string | null
  lastStatus: string | null
  /** Most recently captured provider-reported balance, or null if none is stored. */
  balance: number | null
}

export interface AccountGroup {
  connectionId: string
  sourceType: string
  accounts: AccountInfo[]
}
