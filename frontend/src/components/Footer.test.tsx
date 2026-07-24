import { render, screen } from '@testing-library/react'
import { Provider } from 'react-redux'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { store } from '../app/store'
import Footer from './Footer'

function renderFooter(initialPath = '/') {
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Footer />
      </MemoryRouter>
    </Provider>,
  )
}

describe('Footer build id', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('shows the commit hash and build date when both are injected', () => {
    vi.stubEnv('VITE_BUILD_ID', 'abc1234')
    vi.stubEnv('VITE_BUILD_DATE', '2026-07-15')
    renderFooter()
    expect(screen.getByText('abc1234 · 2026-07-15')).toBeInTheDocument()
  })

  it('shows the hash alone when no build date is injected', () => {
    vi.stubEnv('VITE_BUILD_ID', 'abc1234')
    vi.stubEnv('VITE_BUILD_DATE', '')
    renderFooter()
    expect(screen.getByText('abc1234')).toBeInTheDocument()
  })

  it('falls back to "dev" when no build id is injected', () => {
    vi.stubEnv('VITE_BUILD_ID', '')
    vi.stubEnv('VITE_BUILD_DATE', '')
    renderFooter()
    expect(screen.getByText('dev')).toBeInTheDocument()
  })
})

describe('Footer Kibana link', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('renders a Logs link to the configured Kibana URL, opening in a new tab', () => {
    vi.stubEnv('VITE_KIBANA_URL', 'http://claw.tail303da.ts.net:5601/app/home')
    renderFooter()
    const link = screen.getByRole('link', { name: 'Logs' })
    expect(link).toHaveAttribute('href', 'http://claw.tail303da.ts.net:5601/app/home')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })

  it('renders no Logs link when the Kibana URL is not configured', () => {
    vi.stubEnv('VITE_KIBANA_URL', '')
    renderFooter()
    expect(screen.queryByRole('link', { name: 'Logs' })).not.toBeInTheDocument()
  })
})

describe('Footer navigation', () => {
  it('links to the Accounts page from the budget page', () => {
    renderFooter('/')
    const link = screen.getByRole('link', { name: 'Accounts' })
    expect(link).toHaveAttribute('href', '/accounts')
  })

  it('links back to the budget page from the Accounts page', () => {
    renderFooter('/accounts')
    const link = screen.getByRole('link', { name: 'Budget' })
    expect(link).toHaveAttribute('href', '/')
  })
})
