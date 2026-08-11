import { useMemo, useState, type ReactNode } from 'react'
import { parseJsonPayload } from '../utils/jsonPayload'

interface JsonDisclosureProps {
  header: ReactNode
  trailing?: ReactNode
  rawJson: string
  defaultExpanded?: boolean
}

/** A collapsible row that reveals a pretty-printed (or malformed-fallback) JSON payload on click. */
export default function JsonDisclosure({ header, trailing, rawJson, defaultExpanded = false }: JsonDisclosureProps) {
  const [expanded, setExpanded] = useState(defaultExpanded)
  // Re-parsed only when the payload itself changes, not on every unrelated re-render of an
  // already-expanded row (a sibling row toggling, a parent list appending a page, ...).
  const { text } = useMemo(() => parseJsonPayload(rawJson), [rawJson])

  return (
    <div className="border-b border-divider last:border-b-0">
      <button
        onClick={() => setExpanded((e) => !e)}
        aria-expanded={expanded}
        className="flex w-full items-center gap-2 px-3 py-2 text-left hover:text-accent"
      >
        <span aria-hidden="true" className="text-xs text-muted">{expanded ? '▾' : '▸'}</span>
        <span className="min-w-0 flex-1 truncate font-mono text-xs text-text">{header}</span>
        {trailing}
      </button>
      {expanded && (
        <pre className="whitespace-pre-wrap break-words border-t border-divider bg-[var(--app-bg)] px-3 py-2 font-mono text-sm text-text">
          {text}
        </pre>
      )}
    </div>
  )
}
