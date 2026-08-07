import { useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft, faTriangleExclamation } from '@fortawesome/free-solid-svg-icons'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { fetchAccounts, findAccount } from '../features/accounts/accountsSlice'
import { fetchSyncArchive, clearSyncArchive } from '../features/syncArchive/syncArchiveSlice'
import JsonDisclosure from '../components/JsonDisclosure'
import { formatInstant } from '../utils/format'
import type { SyncArchiveRun } from '../types/syncArchive'

// Same remount-on-switch shape as AccountTransactionsPage: keying on the id gives the new account
// fresh state instead of carrying the previous one's runs and expand state across the navigation.
export default function SyncArchivePage() {
  const { accountId } = useParams<{ accountId: string }>()
  return <SyncArchive key={accountId} accountId={accountId} />
}

function SyncArchive({ accountId }: { accountId: string | undefined }) {
  const dispatch = useAppDispatch()
  const id = Number(accountId)
  const { runs, nextBefore, loading, loadingMore, error } = useAppSelector((state) => state.syncArchive)
  const { groups } = useAppSelector((state) => state.accounts)
  const account = findAccount(groups, id)

  // The archive endpoint has no account name or sourceType of its own — both come from the
  // accounts list, which a direct link into this page (rather than via AccountTransactionsPage)
  // may not have loaded yet.
  useEffect(() => {
    if (groups.length === 0) {
      dispatch(fetchAccounts())
    }
  }, [dispatch, groups.length])

  useEffect(() => {
    if (!Number.isNaN(id)) {
      dispatch(fetchSyncArchive({ accountId: id }))
    }
  }, [dispatch, id])

  useEffect(() => {
    return () => {
      dispatch(clearSyncArchive())
    }
  }, [dispatch])

  // This is a full page read rather than a side-panel one, so unlike RawHistoryTab a failure gets
  // a toast in addition to the inline message. Tracked by message so a retry that fails the same
  // way still re-announces, but a re-render for an unrelated reason does not repeat it.
  const toastedFor = useRef<string | null>(null)
  useEffect(() => {
    if (error && error !== toastedFor.current) {
      toastedFor.current = error
      toast.error(error)
    } else if (!error) {
      toastedFor.current = null
    }
  }, [error])

  const handleLoadMore = () => {
    if (!Number.isNaN(id) && nextBefore && !loadingMore) {
      dispatch(fetchSyncArchive({ accountId: id, before: nextBefore }))
    }
  }

  const handleRetry = () => {
    if (!Number.isNaN(id)) {
      dispatch(fetchSyncArchive({ accountId: id }))
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-bg pb-14 text-text">
      <div className="relative flex items-center border-b-2 border-divider px-4 py-3">
        <Link
          to={`/accounts/${accountId}`}
          className="btn btn-ghost text-[13px]"
          title="Back to transactions"
        >
          <FontAwesomeIcon icon={faArrowLeft} className="h-[13px] w-[13px]" />
          <span>Transactions</span>
        </Link>
        <h2 className="absolute left-1/2 -translate-x-1/2 text-[18px]">
          {account ? `${account.info.name} — Sync Archive` : 'Sync Archive'}
        </h2>
      </div>

      <div className="mx-auto w-full max-w-[860px] px-4 py-6">
        {loading && runs.length === 0 && (
          <div className="text-muted py-12 text-center">Loading...</div>
        )}

        {!loading && error && runs.length === 0 && (
          <div className="py-12 text-center">
            <p className="text-accent">{error}</p>
            <button onClick={handleRetry} className="btn btn-secondary mt-3">
              Retry
            </button>
          </div>
        )}

        {!loading && !error && runs.length === 0 && (
          <div className="text-muted py-12 text-center">
            {account?.sourceType === 'Manual'
              ? "This account doesn't sync, so there is nothing archived for it."
              : 'This account has never synced.'}
          </div>
        )}

        {runs.length > 0 && (
          <>
            <div className="border border-divider bg-surface shadow-elev-sm">
              {runs.map((run) => (
                <SyncArchiveRunRow key={run.syncRunId} run={run} />
              ))}
            </div>

            {nextBefore && (
              <div className="mt-4 text-center">
                <button onClick={handleLoadMore} disabled={loadingMore} className="btn btn-secondary">
                  {loadingMore ? 'Loading…' : 'Load more'}
                </button>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  )
}

function SyncArchiveRunRow({ run }: { run: SyncArchiveRun }) {
  const [expanded, setExpanded] = useState(false)

  const transactions = run.items.filter((item) => item.itemType === 'Transaction')
  const removed = run.items.filter((item) => item.itemType === 'Removed')
  const errors = run.items.filter((item) => item.itemType === 'ProviderError')

  return (
    <div className="border-b border-divider last:border-b-0">
      <button
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
        className="flex w-full items-center gap-3 px-3 py-2 text-left hover:text-accent"
      >
        <span aria-hidden="true" className="text-xs text-muted">
          {expanded ? '▾' : '▸'}
        </span>
        <span className="font-mono text-xs text-text">{formatInstant(run.syncedAt)}</span>
        <span className="text-xs text-muted">{run.sourceType}</span>
        <span className="ml-auto flex items-center gap-3 text-xs text-muted">
          {run.errorCount > 0 && (
            <span className="flex items-center gap-1 text-accent-700">
              <FontAwesomeIcon icon={faTriangleExclamation} className="h-[11px] w-[11px]" />
              {run.errorCount} error{run.errorCount === 1 ? '' : 's'}
            </span>
          )}
          <span>
            {run.transactionCount} transaction{run.transactionCount === 1 ? '' : 's'}
          </span>
          {run.removedCount > 0 && <span>{run.removedCount} removed</span>}
        </span>
      </button>

      {expanded && (
        <div className="border-t border-divider bg-bg px-3 py-2">
          {run.accountMetadataJson && (
            <div className="mb-2 border border-divider">
              <JsonDisclosure header="Account metadata" rawJson={run.accountMetadataJson} />
            </div>
          )}

          {transactions.length > 0 && (
            <div className="mb-2">
              <p className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-muted">
                {transactions.length} transaction{transactions.length === 1 ? '' : 's'}
              </p>
              <div className="border border-divider">
                {transactions.map((item, index) => (
                  <JsonDisclosure
                    key={item.providerTransactionId ?? index}
                    header={item.providerTransactionId ?? `#${index + 1}`}
                    rawJson={item.rawJson ?? ''}
                  />
                ))}
              </div>
            </div>
          )}

          {removed.length > 0 && (
            <div className="mb-2">
              <p className="mb-1 text-[11px] font-semibold uppercase tracking-wide text-muted">
                {removed.length} removed
              </p>
              <div className="border border-divider">
                {removed.map((item, index) => (
                  <div
                    key={item.providerTransactionId ?? index}
                    className="border-b border-divider px-3 py-2 font-mono text-xs text-text last:border-b-0"
                  >
                    {item.providerTransactionId}
                  </div>
                ))}
              </div>
            </div>
          )}

          {errors.length > 0 && (
            <div>
              <p className="mb-1 flex items-center gap-1 text-[11px] font-semibold uppercase tracking-wide text-accent-700">
                <FontAwesomeIcon icon={faTriangleExclamation} className="h-[10px] w-[10px]" />
                {errors.length} provider error{errors.length === 1 ? '' : 's'}
              </p>
              <div className="border border-divider">
                {errors.map((item, index) => (
                  <JsonDisclosure key={index} header={`Error #${index + 1}`} rawJson={item.rawJson ?? ''} />
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
