import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faArrowLeft, faTriangleExclamation } from '@fortawesome/free-solid-svg-icons'
import toast from 'react-hot-toast'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { useEnsureAccountsLoaded } from '../features/accounts/useEnsureAccountsLoaded'
import { findAccount } from '../features/accounts/accountsSlice'
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
  const {
    accountId: loadedFor,
    runs,
    nextBefore,
    loading,
    loadingMore,
    error,
  } = useAppSelector((state) => state.syncArchive)
  // The archive endpoint has no account name or sourceType of its own — both come from the
  // accounts list, which a direct link into this page (rather than via AccountTransactionsPage)
  // may not have loaded yet.
  const groups = useEnsureAccountsLoaded()
  const account = findAccount(groups, id)

  // Anything on screen before the mount effect below has dispatched belongs to some other
  // account (or nothing yet) — the id the slice recorded is what tells collapsed-vs-loading
  // apart, the same guard RawHistoryTab uses via its own loadedFor. Without it, the first paint
  // (and the transitional render right after switching accounts) would show "never synced" for
  // an account with years of history, one frame before the real fetch has even started.
  const notYetLoaded = loadedFor !== id

  // Toasts fire imperatively at the dispatch site, matching AccountsPage's sync/resync
  // convention, rather than reacting to the stored error — a full-page read gets a toast in
  // addition to the inline message, unlike RawHistoryTab's side-panel one.
  const dispatchFetch = async (args: { accountId: number; before?: string }) => {
    const result = await dispatch(fetchSyncArchive(args))
    if (fetchSyncArchive.rejected.match(result)) {
      toast.error(result.payload as string)
    }
  }

  useEffect(() => {
    if (!Number.isNaN(id)) {
      dispatchFetch({ accountId: id })
    }
    // dispatchFetch is a new closure every render (it captures dispatch, which is stable); adding
    // it here would re-run the fetch on every render instead of only when the account changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [dispatch, id])

  useEffect(() => {
    return () => {
      dispatch(clearSyncArchive())
    }
  }, [dispatch])

  const handleLoadMore = () => {
    if (!Number.isNaN(id) && nextBefore && !loadingMore) {
      dispatchFetch({ accountId: id, before: nextBefore })
    }
  }

  const handleRetry = () => {
    if (!Number.isNaN(id)) {
      dispatchFetch({ accountId: id })
    }
  }

  return (
    <div className="flex min-h-screen flex-col bg-[var(--app-bg)] pb-14 text-text">
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
        {(loading || notYetLoaded) && runs.length === 0 && (
          <div className="text-muted py-12 text-center">Loading...</div>
        )}

        {!loading && !notYetLoaded && error && runs.length === 0 && (
          <div className="py-12 text-center">
            <p className="text-accent">{error}</p>
            <button onClick={handleRetry} className="btn btn-secondary mt-3">
              Retry
            </button>
          </div>
        )}

        {!loading && !notYetLoaded && !error && runs.length === 0 && (
          <div className="text-muted py-12 text-center">
            {account?.sourceType === 'Manual'
              ? "This account doesn't sync, so there is nothing archived for it."
              : 'This account has never synced.'}
          </div>
        )}

        {runs.length > 0 && (
          <>
            <div className="card">
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

  return (
    <div className="border-b border-divider last:border-b-0">
      <button
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
        className="flex w-full items-center gap-3 px-3 py-2 text-left hover:text-accent"
      >
        <span
          aria-hidden="true"
          className={`inline-block text-xs text-muted transition-transform duration-[120ms] ease-[ease] ${
            expanded ? 'rotate-90' : ''
          }`}
        >
          ▸
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
          {run.skippedCount > 0 && <span>{run.skippedCount} skipped</span>}
        </span>
      </button>

      {expanded && <SyncArchiveRunItems run={run} />}
    </div>
  )
}

// Split out so the item-type filtering below only ever runs while a run is actually expanded —
// a collapsed row has no use for it.
function SyncArchiveRunItems({ run }: { run: SyncArchiveRun }) {
  const transactions = run.items.filter((item) => item.itemType === 'Transaction')
  const removed = run.items.filter((item) => item.itemType === 'Removed')
  const errors = run.items.filter((item) => item.itemType === 'ProviderError')
  const skipped = run.items.filter((item) => item.itemType === 'SkippedTransaction')

  return (
    <div className="border-t border-divider px-3 py-2">
      {run.accountMetadataJson && (
        <div className="mb-2 border border-divider">
          <JsonDisclosure header="Account metadata" rawJson={run.accountMetadataJson} />
        </div>
      )}

      {transactions.length > 0 && (
        <div className="mb-2">
          <p className="mb-1 text-[13px] font-semibold uppercase tracking-wide text-muted">
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
          <p className="mb-1 text-[13px] font-semibold uppercase tracking-wide text-muted">
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
        <div className={skipped.length > 0 ? 'mb-2' : ''}>
          <p className="mb-1 flex items-center gap-1 text-[13px] font-semibold uppercase tracking-wide text-accent-700">
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

      {skipped.length > 0 && (
        <div>
          <p className="mb-1 text-[13px] font-semibold uppercase tracking-wide text-muted">
            {skipped.length} skipped
          </p>
          <div className="border border-divider">
            {skipped.map((item, index) => (
              <JsonDisclosure key={index} header={`Skipped #${index + 1}`} rawJson={item.rawJson ?? ''} />
            ))}
          </div>
        </div>
      )}
    </div>
  )
}
