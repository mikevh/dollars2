export function formatCurrency(amount: number): string {
  return amount.toLocaleString('en-US', { style: 'currency', currency: 'USD' })
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
  const minutes = Math.floor(seconds / 60)
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
