import { useEffect } from 'react'
import { Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { fetchAccounts, syncConnection } from '../features/accounts/accountsSlice'
import { formatCurrency, formatRelativeTime } from '../utils/format'
import type { AccountGroup, AccountInfo } from '../types/account'

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
    const results = result.payload
    const failures = results.filter((r) => r.status === 'Failure')
    if (failures.length > 0) {
      toast.error(`Sync failed for ${failures.map((f) => f.accountName).join(', ')}`)
      return
    }
    const total = results.reduce((sum, r) => sum + r.transactionCount, 0)
    toast.success(total > 0 ? `Synced ${total} new transaction${total === 1 ? '' : 's'}` : 'Synced — no new transactions')
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
                  {group.sourceType !== 'Manual' && <SyncButton group={group} />}
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
