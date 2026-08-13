/** One archived item within a run. Which fields are populated depends on `itemType`. */
export interface SyncArchiveItem {
  /** 'Transaction' | 'Removed' | 'AccountMetadata' | 'ProviderError' | 'SkippedTransaction' */
  itemType: string
  /** Set on Transaction and Removed items; null on the rest, which have no id. */
  providerTransactionId: string | null
  /** The provider's payload, verbatim. Null on Removed items, whose entire payload is the id. */
  rawJson: string | null
}

/** Everything one sync run archived for one account: a summary line plus every raw payload it wrote. */
export interface SyncArchiveRun {
  syncRunId: string
  syncedAt: string
  sourceType: string
  transactionCount: number
  removedCount: number
  errorCount: number
  skippedCount: number
  /** The run's account-metadata payload, hoisted out of `items` for convenience. Null if none archived. */
  accountMetadataJson: string | null
  items: SyncArchiveItem[]
}

/** One page of an account's sync archive, newest run first. */
export interface AccountSyncArchive {
  accountName: string
  /** The provider this account syncs from, e.g. 'SimpleFIN', 'Plaid', or 'Manual'. */
  sourceType: string
  runs: SyncArchiveRun[]
  /** Cursor for the next (older) page — pass back as `before`. Null when there is nothing older. */
  nextBefore: string | null
}
