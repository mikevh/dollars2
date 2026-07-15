import { render, screen } from '@testing-library/react'

// Smoke test proving the Vitest + testing-library + jsdom harness runs.
// Replace or add real component tests as features land.
describe('test harness', () => {
  it('renders and queries the DOM', () => {
    render(<h1>Dollars2</h1>)
    expect(screen.getByRole('heading', { name: 'Dollars2' })).toBeInTheDocument()
  })
})
