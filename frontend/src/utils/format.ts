export function formatCurrency(amount: number): string {
  return amount.toLocaleString('en-US', { style: 'currency', currency: 'USD' })
}

/** "2026-08-03 06:00 UTC" — for a raw provider archive, keyed by instant, shown as one. */
export function formatInstant(iso: string): string {
  const at = new Date(iso)
  if (Number.isNaN(at.getTime())) {
    return iso
  }
  const pad = (n: number) => String(n).padStart(2, '0')
  const day = `${at.getUTCFullYear()}-${pad(at.getUTCMonth() + 1)}-${pad(at.getUTCDate())}`
  return `${day} ${pad(at.getUTCHours())}:${pad(at.getUTCMinutes())} UTC`
}

/**
 * Human-friendly "time ago" for a sync timestamp, e.g. "just now", "5m ago", "2h ago", "3d ago".
 * Falls back to an absolute date once older than a week. `now` is injectable for deterministic tests.
 */
export function formatRelativeTime(iso: string, now: number = Date.now()): string {
  const then = new Date(iso).getTime()
  const seconds = Math.floor((now - then) / 1000)

  if (seconds < 45) {
    return 'just now'
  }
  // The 45s 'just now' cutoff is meant to round up, so clamp to 1 — a bare floor would
  // report "0m ago" for 45..59 seconds.
  const minutes = Math.max(1, Math.floor(seconds / 60))
  if (minutes < 60) {
    return `${minutes}m ago`
  }
  const hours = Math.floor(minutes / 60)
  if (hours < 24) {
    return `${hours}h ago`
  }
  const days = Math.floor(hours / 24)
  if (days < 7) {
    return `${days}d ago`
  }
  return new Date(iso).toLocaleDateString()
}
