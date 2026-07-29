import { useEffect, useId, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import Dialog, { DialogHeader } from '../components/Dialog'
import { fetchAccounts, syncConnection, resyncConnection } from '../features/accounts/accountsSlice'
import { formatCurrency, formatRelativeTime } from '../utils/format'
import type { AccountGroup, AccountInfo, SyncResult } from '../types/account'

// Matches formatRelativeTime's finest granularity ('just now' vs whole minutes), so the
// 'just now' -> '1m ago' transition doesn't visibly lag.
const RELATIVE_TIME_TICK_MS = 30_000

/** Current epoch millis, re-rendering on an interval so rendered relative times age on their own. */
function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(id)
  }, [intervalMs])

  return now
}

/** Shared success/failure toast for both the manual sync and the full resync. */
function toastSyncResults(results: SyncResult[]) {
  const failures = results.filter((r) => r.status === 'Failure')
  if (failures.length > 0) {
    toast.error(`Sync failed for ${failures.map((f) => f.accountName).join(', ')}`)
    return
  }
  const total = results.reduce((sum, r) => sum + r.transactionCount, 0)
  toast.success(total > 0 ? `Synced ${total} new transaction${total === 1 ? '' : 's'}` : 'Synced — no new transactions')
}

function sourceTypeLabel(sourceType: string): string {
  if (sourceType === 'Manual') {
    return 'Manual accounts'
  }
  return sourceType
}

function LastSynced({ account, now }: { account: AccountInfo; now: number }) {
  if (!account.lastSyncedAt) {
    return <span className="text-muted">—</span>
  }

  const failed = account.lastStatus === 'Failure'
  const absolute = new Date(account.lastSyncedAt).toLocaleString()
  return (
    <span
      className={failed ? 'text-accent' : 'text-muted'}
      title={failed ? `Last sync failed · ${absolute}` : absolute}
    >
      {failed ? 'sync failed ' : 'synced '}
      {formatRelativeTime(account.lastSyncedAt, now)}
    </span>
  )
}

function SyncButton({ group }: { group: AccountGroup }) {
  const dispatch = useAppDispatch()
  const syncingConnectionId = useAppSelector((state) => state.accounts.syncingConnectionId)
  const syncing = syncingConnectionId === group.connectionId
  // A sync is in progress on another group; the server serializes per user, so disable the rest.
  const otherSyncing = syncingConnectionId !== null && !syncing

  const handleSync = async () => {
    const result = await dispatch(syncConnection(group.connectionId))
    if (syncConnection.rejected.match(result)) {
      toast.error(result.payload as string)
      return
    }
    toastSyncResults(result.payload)
  }

  return (
    <button
      type="button"
      onClick={handleSync}
      disabled={syncing || otherSyncing}
      className="text-[12px] font-semibold uppercase tracking-wide text-accent disabled:cursor-not-allowed disabled:opacity-50"
    >
      {syncing ? 'Syncing…' : 'Sync'}
    </button>
  )
}

function ResyncButton({ group }: { group: AccountGroup }) {
  const [open, setOpen] = useState(false)
  const syncingConnectionId = useAppSelector((state) => state.accounts.syncingConnectionId)
  // Any sync in progress serializes per user, so disable resync while one runs.
  const anySyncing = syncingConnectionId !== null

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        disabled={anySyncing}
        className="text-[12px] font-semibold uppercase tracking-wide text-accent disabled:cursor-not-allowed disabled:opacity-50"
      >
        Re-sync
      </button>
      {open && <ResyncDialog group={group} onClose={() => setOpen(false)} />}
    </>
  )
}

function ResyncDialog({ group, onClose }: { group: AccountGroup; onClose: () => void }) {
  const dispatch = useAppDispatch()
  const titleId = useId()
  // Focus the confirm button rather than the panel: with the days input gone there's nothing to
  // fill in, and this keeps Enter-to-confirm working via the button's native activation.
  const confirmRef = useRef<HTMLButtonElement>(null)
  const syncingConnectionId = useAppSelector((state) => state.accounts.syncingConnectionId)
  const resyncing = syncingConnectionId === group.connectionId

  const handleResync = async () => {
    const result = await dispatch(resyncConnection(group.connectionId))
    if (resyncConnection.rejected.match(result)) {
      toast.error(result.payload as string)
      return
    }
    toastSyncResults(result.payload)
    onClose()
  }

  return (
    <Dialog onClose={onClose} labelledBy={titleId} initialFocusRef={confirmRef} className="max-w-sm">
      <DialogHeader
        id={titleId}
        title={<>Re-sync {sourceTypeLabel(group.sourceType)}</>}
        onClose={onClose}
      />

      <p className="text-sm text-text">
        Re-fetches this connection's full transaction history. Transactions you already have are
        matched and skipped, so this only adds what's missing.
      </p>

      <div className="mt-5 flex justify-end gap-2">
        <button onClick={onClose} className="btn btn-secondary">
          Cancel
        </button>
        <button
          ref={confirmRef}
          onClick={handleResync}
          disabled={resyncing}
          className="btn btn-primary"
        >
          {resyncing ? 'Re-syncing…' : 'Re-sync'}
        </button>
      </div>
    </Dialog>
  )
}

export default function AccountsPage() {
  const dispatch = useAppDispatch()
  const { groups, loading, error } = useAppSelector((state) => state.accounts)
  const now = useNow(RELATIVE_TIME_TICK_MS)

  useEffect(() => {
    dispatch(fetchAccounts())
  }, [dispatch])

  return (
    <div className="flex min-h-screen flex-col bg-bg pb-14 text-text">
      <div className="relative flex items-center border-b-2 border-divider px-4 py-3">
        <span className="font-heading text-[16px] font-extrabold">Dollars2</span>
        <h2 className="absolute left-1/2 -translate-x-1/2 text-[18px]">Accounts</h2>
      </div>

      <div className="mx-auto w-full max-w-[720px] px-4 py-6">
        {loading && <div className="text-muted py-12 text-center">Loading...</div>}

        {!loading && error && <div className="py-12 text-center text-accent">{error}</div>}

        {!loading && !error && groups.length === 0 && (
          <div className="text-muted py-12 text-center">No accounts.</div>
        )}

        {!loading && !error && groups.length > 0 && (
          <div className="space-y-4">
            {groups.map((group) => (
              <div key={group.connectionId} className="border border-divider bg-surface shadow-elev-sm">
                <div className="flex items-center justify-between border-b-2 border-divider px-4 py-2">
                  <span className="text-muted text-[12px] font-semibold uppercase tracking-wide">
                    {sourceTypeLabel(group.sourceType)}
                  </span>
                  {group.sourceType !== 'Manual' && (
                    <div className="flex items-center gap-3">
                      <SyncButton group={group} />
                      <ResyncButton group={group} />
                    </div>
                  )}
                </div>
                <ul>
                  {group.accounts.map((account) => (
                    <li key={account.id}>
                      <Link
                        to={`/accounts/${account.id}`}
                        className="flex items-center justify-between px-4 py-2.5 text-[14px] hover:bg-[color-mix(in_srgb,var(--color-text)_6%,transparent)]"
                        title={`View ${account.name} transactions`}
                      >
                        <span>{account.name}</span>
                        <div className="flex flex-col items-end gap-0.5">
                          {account.balance !== null && (
                            <span className="font-medium tabular-nums">
                              {formatCurrency(account.balance)}
                            </span>
                          )}
                          <LastSynced account={account} now={now} />
                        </div>
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
