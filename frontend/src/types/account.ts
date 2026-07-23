export interface AccountInfo {
  id: number
  name: string
  lastSyncedAt: string | null
  lastStatus: string | null
}

export interface AccountGroup {
  connectionId: string
  sourceType: string
  accounts: AccountInfo[]
}
