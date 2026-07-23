import { useEffect } from 'react'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { fetchAccounts } from '../features/accounts/accountsSlice'
import { formatCurrency, formatRelativeTime } from '../utils/format'
import type { AccountInfo } from '../types/account'

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
                <div className="text-muted border-b-2 border-divider px-4 py-2 text-[12px] font-semibold uppercase tracking-wide">
                  {sourceTypeLabel(group.sourceType)}
                </div>
                <ul>
                  {group.accounts.map((account) => (
                    <li
                      key={account.id}
                      className="flex items-center justify-between px-4 py-2.5 text-[14px]"
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
