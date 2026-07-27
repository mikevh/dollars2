import { render, screen } from '@testing-library/react'
import { Provider } from 'react-redux'
import { describe, expect, it, vi } from 'vitest'
import { store } from '../../app/store'
import type { LineItemResponse } from '../../types/budget'
import LineItemRow from './LineItemRow'

vi.mock('../../api/client', () => ({
  api: {
    get: vi.fn(() => Promise.resolve({ data: [], error: null })),
    put: vi.fn(() => Promise.resolve({ data: {}, error: null })),
    delete: vi.fn(() => Promise.resolve({ data: true, error: null })),
  },
}))

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn() },
}))

function makeLineItem(overrides: Partial<LineItemResponse> = {}): LineItemResponse {
  return {
    id: 100,
    name: 'Rent',
    plannedAmount: 200,
    spentAmount: 50,
    receivedAmount: 0,
    rolloverAmount: 0,
    sortOrder: 0,
    notes: null,
    ...overrides,
  }
}

function renderRow(props: Partial<Parameters<typeof LineItemRow>[0]> = {}) {
  render(
    <Provider store={store}>
      <LineItemRow lineItem={makeLineItem()} groupId={1} isIncome={false} {...props} />
    </Provider>,
  )
}

describe('LineItemRow name editing', () => {
  it('renders the name as text when not flagged for editing', () => {
    renderRow()
    expect(screen.getByText('Rent')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('mounts with the name editor open and empty when startEditing is set', () => {
    renderRow({ startEditing: true })
    const input = screen.getByRole('textbox')
    expect(input).toHaveValue('')
    expect(input).toHaveFocus()
  })
})
