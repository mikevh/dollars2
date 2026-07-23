import { render, screen, fireEvent, within, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { LineItemResponse } from '../../types/budget'
import { api } from '../../api/client'
import ActivityPane from './ActivityPane'

// ActivityPane fetches its scoped transactions on mount; stub the client so the
// list resolves empty and the component settles out of its loading state.
vi.mock('../../api/client', () => ({
  api: {
    get: vi.fn(() => Promise.resolve({ data: [], error: null })),
    put: vi.fn(() => Promise.resolve({ data: {}, error: null })),
  },
}))

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn() },
}))

beforeEach(() => {
  vi.clearAllMocks()
})

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

function renderPane(lineItem: LineItemResponse, props: Partial<Parameters<typeof ActivityPane>[0]> = {}) {
  const onClose = vi.fn()
  render(
    <ActivityPane
      lineItem={lineItem}
      isIncome={false}
      budgetMonth={7}
      onClose={onClose}
      {...props}
    />,
  )
  return { onClose }
}

describe('ActivityPane (Modernist restyle)', () => {
  it('renders the line-item name header and closes on the X button', () => {
    const { onClose } = renderPane(makeLineItem())
    expect(screen.getByRole('heading', { name: 'Rent' })).toBeInTheDocument()
    fireEvent.click(screen.getByTitle('Close'))
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('renders a negative remaining amount in accent-red', () => {
    // planned 200 + rollover 0 - spent 250 = -50 remaining
    renderPane(makeLineItem({ spentAmount: 250 }))
    const remaining = screen.getByText('-$50.00')
    expect(remaining.className).toContain('text-accent-700')
    expect(remaining.className).toContain('tabular-nums')
  })

  it('renders a healthy remaining amount in calm (non-accent) text', () => {
    // planned 200 + rollover 0 - spent 50 = 150 remaining
    renderPane(makeLineItem())
    const remaining = screen.getByText('$150.00')
    expect(remaining.className).toContain('text-text')
    expect(remaining.className).not.toContain('text-accent-700')
  })

  it('renders a negative rollover row with a minus sign in accent-red (no green/red palette)', async () => {
    renderPane(makeLineItem({ rolloverAmount: -30 }))
    // budgetMonth 7 (July) → rollover carried from June
    const label = await screen.findByText('Rollover from June')
    const row = label.parentElement as HTMLElement
    const amount = within(row).getByText('-$30.00')
    expect(amount.className).toContain('text-accent-700')
    expect(amount.className).toContain('tabular-nums')
  })

  it('renders a positive rollover row with a plus sign in calm text', async () => {
    renderPane(makeLineItem({ rolloverAmount: 30 }))
    const label = await screen.findByText('Rollover from June')
    const row = label.parentElement as HTMLElement
    const amount = within(row).getByText('+$30.00')
    expect(amount.className).toContain('text-text')
    expect(amount.className).not.toContain('text-accent-700')
  })
})

describe('ActivityPane notes editor', () => {
  it('seeds the textarea from lineItem.notes', () => {
    renderPane(makeLineItem({ notes: 'water bill due the 5th' }))
    expect(screen.getByLabelText('Notes')).toHaveValue('water bill due the 5th')
  })

  it('renders an empty textarea when notes is null', () => {
    renderPane(makeLineItem({ notes: null }))
    expect(screen.getByLabelText('Notes')).toHaveValue('')
  })

  it('hides Save/Cancel until the textarea is edited, then shows them', () => {
    renderPane(makeLineItem({ notes: 'seed' }))
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Notes'), { target: { value: 'seed edited' } })

    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
  })

  it('Save issues the PUT with { name, plannedAmount, notes } and calls onBudgetMutate', async () => {
    const onBudgetMutate = vi.fn()
    renderPane(makeLineItem({ notes: null }), { onBudgetMutate })

    fireEvent.change(screen.getByLabelText('Notes'), { target: { value: 'new note' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(api.put).toHaveBeenCalledWith('/api/line-items/100', {
      name: 'Rent',
      plannedAmount: 200,
      notes: 'new note',
    })
    await waitFor(() => expect(onBudgetMutate).toHaveBeenCalledTimes(1))
  })

  it('Cancel reverts the textarea to the saved value and hides the buttons', () => {
    renderPane(makeLineItem({ notes: 'original' }))

    fireEvent.change(screen.getByLabelText('Notes'), { target: { value: 'changed' } })
    expect(screen.getByLabelText('Notes')).toHaveValue('changed')

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.getByLabelText('Notes')).toHaveValue('original')
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
  })
})
