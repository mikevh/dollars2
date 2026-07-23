import { describe, expect, it } from 'vitest'
import { formatRelativeTime } from './format'

describe('formatRelativeTime', () => {
  const now = new Date('2026-07-22T12:00:00Z').getTime()
  const ago = (seconds: number) => new Date(now - seconds * 1000).toISOString()

  it('returns "just now" for very recent timestamps', () => {
    expect(formatRelativeTime(ago(10), now)).toBe('just now')
  })

  it('returns minutes for sub-hour ages', () => {
    expect(formatRelativeTime(ago(5 * 60), now)).toBe('5m ago')
  })

  it('returns hours for sub-day ages', () => {
    expect(formatRelativeTime(ago(2 * 3600), now)).toBe('2h ago')
  })

  it('returns days for sub-week ages', () => {
    expect(formatRelativeTime(ago(3 * 86400), now)).toBe('3d ago')
  })

  it('falls back to an absolute date once older than a week', () => {
    const iso = ago(10 * 86400)
    expect(formatRelativeTime(iso, now)).toBe(new Date(iso).toLocaleDateString())
  })
})
