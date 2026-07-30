import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Provider } from 'react-redux'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../api/client'
import { store } from '../../app/store'
import type { BudgetGroupResponse } from '../../types/budget'
import BudgetGroupCard from './BudgetGroupCard'

vi.mock('../../api/client', () => ({
  api: {
    get: vi.fn(() => Promise.resolve({ data: [], error: null })),
    post: vi.fn(() => Promise.resolve({ data: {}, error: null })),
    put: vi.fn(() => Promise.resolve({ data: {}, error: null })),
    delete: vi.fn(() => Promise.resolve({ data: true, error: null })),
  },
}))

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn() },
}))

// No line items, so the card renders without the droppable rows that would need a DndContext.
function makeGroup(overrides: Partial<BudgetGroupResponse> = {}): BudgetGroupResponse {
  return {
    id: 20,
    name: 'Housing',
    isIncome: false,
    sortOrder: 1,
    lineItems: [],
    ...overrides,
  }
}

function renderCard() {
  const { rerender } = render(
    <Provider store={store}>
      <BudgetGroupCard group={makeGroup()} />
    </Provider>,
  )
  return {
    // Re-render in place — the point of these tests is a prop change with no remount.
    setGroup: (group: BudgetGroupResponse) =>
      rerender(
        <Provider store={store}>
          <BudgetGroupCard group={group} />
        </Provider>,
      ),
  }
}

beforeEach(() => {
  vi.mocked(api.put).mockClear()
})

describe('BudgetGroupCard draft re-sync', () => {
  it('seeds the name editor from the latest prop after an in-place rename', () => {
    const { setGroup } = renderCard()
    setGroup(makeGroup({ name: 'Utilities' }))

    fireEvent.click(screen.getByRole('heading', { name: 'Utilities' }))
    expect(screen.getByRole('textbox')).toHaveValue('Utilities')
  })

  it('issues no update when the editor is opened and blurred without a change', async () => {
    const { setGroup } = renderCard()
    setGroup(makeGroup({ name: 'Utilities' }))

    fireEvent.click(screen.getByRole('heading', { name: 'Utilities' }))
    fireEvent.blur(screen.getByRole('textbox'))

    await vi.waitFor(() => expect(screen.getByRole('heading', { name: 'Utilities' })).toBeInTheDocument())
    expect(vi.mocked(api.put)).not.toHaveBeenCalled()
  })

  it('leaves an open editor alone when the prop changes underneath it', () => {
    const { setGroup } = renderCard()

    fireEvent.click(screen.getByRole('heading', { name: 'Housing' }))
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Home' } })
    setGroup(makeGroup({ name: 'Utilities' }))

    expect(screen.getByRole('textbox')).toHaveValue('Home')
  })
})

describe('BudgetGroupCard add item', () => {
  it('creates a line item and opens it in edit mode on click', async () => {
    const newItem = {
      id: 99,
      name: 'New Item',
      plannedAmount: 0,
      spentAmount: 0,
      receivedAmount: 0,
      rolloverAmount: 0,
      sortOrder: 1,
      notes: null,
    }
    vi.mocked(api.post).mockResolvedValueOnce({ data: newItem, error: null })

    // BudgetGroupCard renders lineItems from its `group` prop, not the store — in the real
    // app the parent re-supplies the prop once the store updates. Simulate that with setGroup,
    // same as the rename tests above; the assertion retries via waitFor until the component's
    // internal editingNewItemId state (set after the dispatch resolves) has caught up.
    const { setGroup } = renderCard()
    fireEvent.click(screen.getByRole('button', { name: '+ Add Item' }))

    expect(vi.mocked(api.post)).toHaveBeenCalledWith('/api/groups/20/line-items', { name: 'New Item', plannedAmount: 0 })

    await waitFor(() => {
      setGroup(makeGroup({ lineItems: [newItem] }))
      expect(screen.getByRole('textbox')).toHaveValue('')
    })
  })
})
