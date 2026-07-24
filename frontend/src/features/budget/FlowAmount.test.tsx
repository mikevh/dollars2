import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import FlowAmount from './FlowAmount'

describe('FlowAmount', () => {
  it('renders a normal expense Spent as a neutral, unsigned figure', () => {
    render(<FlowAmount value={50} isIncome={false} neutralClass="text-muted" />)
    const el = screen.getByText('$50.00')
    expect(el.className).toContain('text-muted')
    expect(el.className).not.toContain('text-positive')
    expect(el.className).not.toContain('text-accent-700')
  })

  it('renders a credit-heavy expense (Spent < 0) as +$ in green', () => {
    // income 690.89 + expense 16.35 assigned → spentAmount -674.54
    render(<FlowAmount value={-674.54} isIncome={false} neutralClass="text-muted" />)
    const el = screen.getByText('+$674.54')
    expect(el.className).toContain('text-positive')
    expect(el.className).not.toContain('text-muted')
  })

  it('renders a normal income Received as a neutral, unsigned figure', () => {
    render(<FlowAmount value={4000} isIncome={true} neutralClass="text-text" />)
    const el = screen.getByText('$4,000.00')
    expect(el.className).toContain('text-text')
    expect(el.className).not.toContain('text-positive')
    expect(el.className).not.toContain('text-accent-700')
  })

  it('renders an adverse income (Received < 0) as -$ in red, not green', () => {
    render(<FlowAmount value={-25} isIncome={true} neutralClass="text-text" />)
    const el = screen.getByText('-$25.00')
    expect(el.className).toContain('text-accent-700')
    expect(el.className).not.toContain('text-positive')
  })

  it('applies the passed layout className alongside the tone class', () => {
    render(<FlowAmount value={-5} isIncome={false} neutralClass="text-muted" className="tabular-nums" />)
    const el = screen.getByText('+$5.00')
    expect(el.className).toContain('tabular-nums')
    expect(el.className).toContain('text-positive')
  })
})
