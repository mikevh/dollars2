import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { Provider } from 'react-redux'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../api/client'
import { store } from '../../app/store'
import type { BudgetGroupResponse, LineItemResponse } from '../../types/budget'
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
    sortOrder: 1,
    lineItems: [],
    ...overrides,
  }
}

function makeLineItem(overrides: Partial<LineItemResponse> = {}): LineItemResponse {
  return {
    id: 1,
    name: 'Item',
    plannedAmount: 0,
    isIncome: false,
    spentAmount: 0,
    receivedAmount: 0,
    rolloverAmount: 0,
    sortOrder: 0,
    notes: null,
    ...overrides,
  }
}

function renderCard(group: BudgetGroupResponse = makeGroup()) {
  const { rerender } = render(
    <Provider store={store}>
      <BudgetGroupCard group={group} />
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
  vi.mocked(api.post).mockClear()
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

describe('BudgetGroupCard name editor focus', () => {
  it('focuses and selects the name editor text once it opens', async () => {
    renderCard()
    fireEvent.click(screen.getByRole('heading', { name: 'Housing' }))

    const input = screen.getByRole('textbox') as HTMLInputElement
    await waitFor(() => expect(input).toHaveFocus())
    expect(input.selectionStart).toBe(0)
    expect(input.selectionEnd).toBe(input.value.length)
  })
})

describe('BudgetGroupCard add item', () => {
  it('creates a line item and opens it in edit mode on click', async () => {
    const newItem = {
      id: 99,
      name: 'New Item',
      plannedAmount: 0,
      isIncome: false,
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
    fireEvent.click(screen.getByRole('button', { name: '+ Add item' }))

    // The group starts with no line items, so isIncome inherits the "empty group → expense" default.
    expect(vi.mocked(api.post)).toHaveBeenCalledWith('/api/groups/20/line-items', { name: 'New Item', plannedAmount: 0, isIncome: false })

    await waitFor(() => {
      setGroup(makeGroup({ lineItems: [newItem] }))
      expect(screen.getByRole('textbox')).toHaveValue('')
    })
  })
})

describe('BudgetGroupCard income inference', () => {
  it('offers "Received" as the metric alternative when every item in the group is income', () => {
    renderCard(makeGroup({ lineItems: [makeLineItem({ isIncome: true })] }))
    fireEvent.click(screen.getByRole('button', { name: /Remaining/ }))
    expect(screen.getByText('Received')).toBeInTheDocument()
  })

  it('offers "Spent" as the metric alternative for an empty group', () => {
    renderCard(makeGroup({ lineItems: [] }))
    fireEvent.click(screen.getByRole('button', { name: /Remaining/ }))
    expect(screen.getByText('Spent')).toBeInTheDocument()
  })

  it('offers "Spent" as the metric alternative when the group mixes income and expense items', () => {
    renderCard(makeGroup({ lineItems: [makeLineItem({ id: 1, isIncome: true }), makeLineItem({ id: 2, isIncome: false })] }))
    fireEvent.click(screen.getByRole('button', { name: /Remaining/ }))
    expect(screen.getByText('Spent')).toBeInTheDocument()
  })

  it('"+ Add income" in an all-income group posts isIncome: true', () => {
    renderCard(makeGroup({ lineItems: [makeLineItem({ isIncome: true })] }))
    fireEvent.click(screen.getByRole('button', { name: '+ Add income' }))
    expect(vi.mocked(api.post)).toHaveBeenCalledWith('/api/groups/20/line-items', { name: 'New Item', plannedAmount: 0, isIncome: true })
  })

  // Regression: handleAddItem previously inherited from lineItems[0] directly instead of the
  // isAllIncome check used for the dropdown label, so a mixed group whose first item happened to
  // be income silently diverged from the "any expense item → expense" rule and posted isIncome: true.
  it('"+ Add item" in a mixed group whose first item is income still posts isIncome: false', () => {
    renderCard(makeGroup({ lineItems: [makeLineItem({ id: 1, isIncome: true }), makeLineItem({ id: 2, isIncome: false })] }))
    fireEvent.click(screen.getByRole('button', { name: '+ Add item' }))
    expect(vi.mocked(api.post)).toHaveBeenCalledWith('/api/groups/20/line-items', { name: 'New Item', plannedAmount: 0, isIncome: false })
  })
})

describe('BudgetGroupCard metric dropdown', () => {
  it('switches the line item and footer figures when a menu option is selected', () => {
    renderCard(makeGroup({ lineItems: [makeLineItem({ id: 1, plannedAmount: 200, spentAmount: 50 })] }))
    // Remaining = planned + rollover - spent = 150, shown once in the row and once in the footer total.
    expect(screen.getAllByText('$150.00')).toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: /Remaining/ }))
    fireEvent.click(screen.getByRole('button', { name: 'Spent' }))

    expect(screen.queryByText('$150.00')).not.toBeInTheDocument()
    expect(screen.getAllByText('$50.00')).toHaveLength(2)
  })

  it('closes the menu on an outside mousedown', () => {
    renderCard(makeGroup({ lineItems: [] }))
    fireEvent.click(screen.getByRole('button', { name: /Remaining/ }))
    expect(screen.getByRole('button', { name: 'Spent' })).toBeInTheDocument()

    fireEvent.mouseDown(document.body)
    expect(screen.queryByRole('button', { name: 'Spent' })).not.toBeInTheDocument()
  })
})

describe('BudgetGroupCard collapse', () => {
  it('replaces the line item rows with an item-count + totals summary when collapsed', () => {
    renderCard(makeGroup({ lineItems: [makeLineItem({ id: 1, name: 'Rent', plannedAmount: 200, spentAmount: 50 })] }))
    fireEvent.click(screen.getByRole('button', { name: 'Collapse group' }))

    expect(screen.queryByText('Rent')).not.toBeInTheDocument()
    expect(screen.getByText('1 items')).toBeInTheDocument()
  })
})
