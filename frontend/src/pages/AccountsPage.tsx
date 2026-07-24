import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { fetchAccounts, syncConnection, resyncConnection } from '../features/accounts/accountsSlice'
import { formatCurrency, formatRelativeTime } from '../utils/format'
import type { AccountGroup, AccountInfo, SyncResult } from '../types/account'

const DEFAULT_RESYNC_DAYS = 180
const MIN_RESYNC_DAYS = 1
const MAX_RESYNC_DAYS = 730

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

function LastSynced({ account }: { account: AccountInfo }) {
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
      {formatRelativeTime(account.lastSyncedAt)}
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
  const [days, setDays] = useState(String(DEFAULT_RESYNC_DAYS))
  const syncingConnectionId = useAppSelector((state) => state.accounts.syncingConnectionId)
  const resyncing = syncingConnectionId === group.connectionId

  const parsed = Number(days)
  const valid = days.trim() !== '' && Number.isInteger(parsed) && parsed >= MIN_RESYNC_DAYS && parsed <= MAX_RESYNC_DAYS

  const handleResync = async () => {
    if (!valid) {
      return
    }
    const result = await dispatch(resyncConnection({ connectionId: group.connectionId, days: parsed }))
    if (resyncConnection.rejected.match(result)) {
      toast.error(result.payload as string)
      return
    }
    toastSyncResults(result.payload)
    onClose()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center" onClick={onClose}>
      <div className="fixed inset-0 bg-black/50" />
      <div
        className="relative w-full max-w-sm border border-divider bg-surface p-6 shadow-elev-lg"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="mb-4 flex items-center justify-between">
          <h2 className="font-heading text-lg font-extrabold uppercase tracking-wide text-text">
            Re-sync {sourceTypeLabel(group.sourceType)}
          </h2>
          <button onClick={onClose} className="text-muted hover:text-accent-700">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5">
              <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
            </svg>
          </button>
        </div>

        <label className="flex flex-col gap-1.5 text-sm text-text">
          Number of days to sync back
          <input
            type="number"
            min={MIN_RESYNC_DAYS}
            max={MAX_RESYNC_DAYS}
            value={days}
            autoFocus
            onChange={(e) => setDays(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && valid) {
                handleResync()
              }
            }}
            className="w-full border border-divider bg-bg px-2 py-1 text-text focus:border-accent focus:outline-none"
          />
        </label>
        <p className="mt-1 text-[12px] text-muted">Between {MIN_RESYNC_DAYS} and {MAX_RESYNC_DAYS} days.</p>

        <div className="mt-5 flex justify-end gap-2">
          <button onClick={onClose} className="btn btn-secondary">
            Cancel
          </button>
          <button
            onClick={handleResync}
            disabled={!valid || resyncing}
            className="btn btn-primary"
          >
            {resyncing ? 'Re-syncing…' : 'Re-sync'}
          </button>
        </div>
      </div>
    </div>
  )
}

export default function AccountsPage() {
  const dispatch = useAppDispatch()
  const { groups, loading, error } = useAppSelector((state) => state.accounts)

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
                          <LastSynced account={account} />
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
