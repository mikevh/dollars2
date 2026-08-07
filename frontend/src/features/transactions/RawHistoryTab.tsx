import { useEffect, useMemo } from 'react'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import JsonDisclosure from '../../components/JsonDisclosure'
import { formatInstant } from '../../utils/format'
import { fetchRawHistory } from './rawHistorySlice'

interface RawHistoryTabProps {
  transactionId: number
  /** Chooses the empty-state wording — a manual transaction has no provider to have heard from. */
  isManual: boolean
}

/**
 * Derived from the payload's own `pending` flag, which both providers carry. Null when the payload
 * doesn't parse or doesn't have one — this view exists to show what the provider actually sent, so it
 * never invents a status.
 */
function statusOf(rawJson: string): string | null {
  try {
    const value: unknown = JSON.parse(rawJson)
    const pending = typeof value === 'object' && value !== null
      ? (value as Record<string, unknown>).pending
      : undefined
    return typeof pending === 'boolean' ? (pending ? 'pending' : 'posted') : null
  } catch {
    return null
  }
}

export default function RawHistoryTab({ transactionId, isManual }: RawHistoryTabProps) {
  const dispatch = useAppDispatch()
  const { transactionId: loadedFor, entries, loading, error } = useAppSelector((state) => state.rawHistory)

  // Lazy load: this runs on the tab's first mount, and the id the slice recorded when the request
  // started keeps a later re-open from asking again.
  useEffect(() => {
    if (loadedFor !== transactionId) {
      dispatch(fetchRawHistory(transactionId))
    }
  }, [dispatch, loadedFor, transactionId])

  // Every row needs its status even while collapsed, and a long-lived transaction's history has no
  // upper bound, so this is computed once per fetch rather than once per render.
  const statuses = useMemo(() => entries.map((entry) => statusOf(entry.rawJson)), [entries])

  // Anything on screen before the effect above has dispatched belongs to some other transaction —
  // an in-flight fetch that resolved after its own dialog closed leaves entries behind with no id.
  // That is "not asked yet", not "nothing archived", so it must not render as an empty archive.
  if (loading || loadedFor !== transactionId) {
    return <p className="py-6 text-center text-sm text-muted">Loading raw history...</p>
  }

  // Inline, never a toast: a failed side-panel read should not interrupt someone mid-edit.
  if (error) {
    return <p className="py-6 text-center text-sm text-accent-700">{error}</p>
  }

  if (entries.length === 0) {
    return (
      <p className="py-6 text-center text-sm text-muted">
        {isManual
          ? 'No provider data — this transaction was entered manually.'
          : 'No archived payloads for this transaction.'}
      </p>
    )
  }

  return (
    <div>
      <p className="mb-2 text-sm text-muted">
        {entries.length} sighting{entries.length === 1 ? '' : 's'} from {entries[0].sourceType}
      </p>
      <div className="max-h-[50vh] overflow-y-auto border border-divider">
        {entries.map((entry, index) => {
          const status = statuses[index]
          return (
            <JsonDisclosure
              // Re-seeded whenever a new fetch lands (loadedFor changes), so the newest sighting
              // starts expanded again rather than carrying over the previous transaction's state.
              key={`${loadedFor}-${index}`}
              defaultExpanded={index === 0}
              rawJson={entry.rawJson}
              header={formatInstant(entry.syncedAt)}
              trailing={
                status && (
                  <span className="font-heading text-[11px] font-extrabold uppercase tracking-wide text-muted">
                    {status}
                  </span>
                )
              }
            />
          )
        })}
      </div>
    </div>
  )
}
