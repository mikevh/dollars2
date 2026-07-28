import { describe, expect, it } from 'vitest'
import { isMoneyDraft } from './money'

describe('isMoneyDraft', () => {
  it('accepts whole dollars and cents', () => {
    expect(isMoneyDraft('0')).toBe(true)
    expect(isMoneyDraft('20')).toBe(true)
    expect(isMoneyDraft('10.9')).toBe(true)
    expect(isMoneyDraft('10.99')).toBe(true)
    expect(isMoneyDraft('1234567.05')).toBe(true)
  })

  it('accepts the in-between states of typing an amount', () => {
    expect(isMoneyDraft('')).toBe(true)
    expect(isMoneyDraft('10.')).toBe(true)
  })

  it('rejects anything finer than a cent', () => {
    expect(isMoneyDraft('10.999')).toBe(false)
    expect(isMoneyDraft('0.005')).toBe(false)
    expect(isMoneyDraft('0.004')).toBe(false)
  })

  it('rejects a sign, since amounts are entered as magnitudes', () => {
    expect(isMoneyDraft('-5')).toBe(false)
    expect(isMoneyDraft('-0.01')).toBe(false)
    expect(isMoneyDraft('+5')).toBe(false)
  })

  it('rejects anything that is not a plain decimal', () => {
    expect(isMoneyDraft('1e3')).toBe(false)
    expect(isMoneyDraft('$5')).toBe(false)
    expect(isMoneyDraft('1,000')).toBe(false)
    expect(isMoneyDraft('abc')).toBe(false)
    expect(isMoneyDraft('1.2.3')).toBe(false)
    expect(isMoneyDraft(' 5')).toBe(false)
  })
})
