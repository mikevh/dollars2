import { useEffect, useState } from 'react'
import { useAppDispatch, useAppSelector } from '../../app/hooks'
import type { RawHistoryEntryResponse } from '../../types/transaction'
import { fetchRawHistory } from './rawHistorySlice'

interface RawHistoryTabProps {
  transactionId: number
  /** Chooses the empty-state wording — a manual transaction has no provider to have heard from. */
  isManual: boolean
}

/** "2026-08-03 06:00 UTC" — the archive is keyed by instant, so it is shown as one. */
function formatSyncedAt(iso: string): string {
  const at = new Date(iso)
  if (Number.isNaN(at.getTime())) {
    return iso
  }
  const pad = (n: number) => String(n).padStart(2, '0')
  const day = `${at.getUTCFullYear()}-${pad(at.getUTCMonth() + 1)}-${pad(at.getUTCDate())}`
  return `${day} ${pad(at.getUTCHours())}:${pad(at.getUTCMinutes())} UTC`
}

interface ParsedPayload {
  /** What to show in the <pre>: pretty-printed when it parses, verbatim when it does not. */
  text: string
  /**
   * Derived from the payload's own `pending` flag, which both providers carry. Null when the
   * payload doesn't parse or doesn't have one — this view exists to show what the provider
   * actually sent, so it never invents a status.
   */
  status: string | null
}

function parsePayload(rawJson: string): ParsedPayload {
  try {
    const parsed: unknown = JSON.parse(rawJson)
    const pending = typeof parsed === 'object' && parsed !== null
      ? (parsed as Record<string, unknown>).pending
      : undefined
    return {
      text: JSON.stringify(parsed, null, 2),
      status: typeof pending === 'boolean' ? (pending ? 'pending' : 'posted') : null,
    }
  } catch {
    // A malformed payload is precisely the thing this view exists to reveal, so it renders
    // as-is rather than taking the dialog down with it.
    return { text: rawJson, status: null }
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

  // Newest sighting open, the rest collapsed. Re-seeded during render rather than in an effect —
  // the same idiom TransactionEditDialog uses to reset state on an input change.
  const [expanded, setExpanded] = useState<Set<number>>(() => new Set([0]))
  const [seededFor, setSeededFor] = useState<RawHistoryEntryResponse[]>(entries)
  if (seededFor !== entries) {
    setSeededFor(entries)
    setExpanded(new Set([0]))
  }

  const toggle = (index: number) => {
    setExpanded((prev) => {
      const next = new Set(prev)
      if (!next.delete(index)) {
        next.add(index)
      }
      return next
    })
  }

  if (loading) {
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
          const isExpanded = expanded.has(index)
          const { text, status } = parsePayload(entry.rawJson)
          return (
            <div key={`${entry.syncRunId}-${index}`} className="border-b border-divider last:border-b-0">
              <button
                onClick={() => toggle(index)}
                aria-expanded={isExpanded}
                className="flex w-full items-center gap-2 px-3 py-2 text-left hover:text-accent"
              >
                <span aria-hidden="true" className="text-xs text-muted">{isExpanded ? '▾' : '▸'}</span>
                <span className="min-w-0 flex-1 truncate font-mono text-xs text-text">
                  {formatSyncedAt(entry.syncedAt)}
                </span>
                {status && (
                  <span className="font-heading text-[11px] font-extrabold uppercase tracking-wide text-muted">
                    {status}
                  </span>
                )}
              </button>
              {isExpanded && (
                <pre className="overflow-x-auto border-t border-divider bg-bg px-3 py-2 font-mono text-xs text-text">
                  {text}
                </pre>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
