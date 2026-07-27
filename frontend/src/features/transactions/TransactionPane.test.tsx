import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { Provider } from 'react-redux'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { store } from '../../app/store'
import { setActiveTab } from './transactionSlice'
import TransactionPane from './TransactionPane'

// Every tab fetch resolves empty so the pane settles immediately.
vi.mock('../../api/client', () => ({
  api: {
    get: vi.fn(() => Promise.resolve({ data: [], error: null })),
    post: vi.fn(() => Promise.resolve({ data: null, error: null })),
    put: vi.fn(() => Promise.resolve({ data: null, error: null })),
    delete: vi.fn(() => Promise.resolve({ data: true, error: null })),
  },
}))

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn() },
}))

beforeEach(() => {
  store.dispatch(setActiveTab('new'))
})

function renderPane() {
  render(
    <Provider store={store}>
      <TransactionPane />
    </Provider>,
  )
  return screen.getByPlaceholderText('Search transactions...')
}

describe('TransactionPane search box', () => {
  it('keeps the typed query while the tab is unchanged', () => {
    const search = renderPane()
    fireEvent.change(search, { target: { value: 'coffee' } })
    expect(search).toHaveValue('coffee')
  })

  it('clears the query when the active tab changes', async () => {
    const search = renderPane()
    fireEvent.change(search, { target: { value: 'coffee' } })
    expect(search).toHaveValue('coffee')

    fireEvent.click(screen.getByRole('button', { name: /Tracked/ }))

    await waitFor(() => expect(search).toHaveValue(''))
  })
})
