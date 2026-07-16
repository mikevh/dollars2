import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Provider } from 'react-redux'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { store } from '../app/store'
import LoginPage from './LoginPage'

function renderLogin() {
  render(
    <Provider store={store}>
      <MemoryRouter>
        <LoginPage />
      </MemoryRouter>
    </Provider>,
  )
}

describe('LoginPage', () => {
  it('renders the Modernist login card copy', () => {
    renderLogin()
    expect(
      screen.getByRole('heading', { name: 'Dollars2' }),
    ).toBeInTheDocument()
    expect(
      screen.getByText('Zero-based budgeting, self-hosted.'),
    ).toBeInTheDocument()
    expect(screen.getByPlaceholderText('you@example.com')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('shows the required-email validation message on empty submit', async () => {
    const user = userEvent.setup()
    renderLogin()
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Email is required')).toBeInTheDocument()
  })

  it('shows the invalid-email validation message for a malformed address', async () => {
    const user = userEvent.setup()
    renderLogin()
    // "a@b" clears the input's native type="email" check (so the form actually
    // submits) but fails the stricter react-hook-form pattern that requires a
    // dotted domain — the path that surfaces the app's own message.
    await user.type(screen.getByLabelText('Email'), 'a@b')
    await user.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Invalid email address')).toBeInTheDocument()
  })
})
