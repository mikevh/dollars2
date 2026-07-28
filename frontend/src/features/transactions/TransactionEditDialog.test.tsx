import { render, screen, fireEvent } from '@testing-library/react'
import { Provider } from 'react-redux'
import { describe, expect, it, vi } from 'vitest'
import { store } from '../../app/store'
import { api } from '../../api/client'
import type { TransactionResponse } from '../../types/transaction'
import TransactionEditDialog from './TransactionEditDialog'

vi.mock('../../api/client', () => ({
  api: {
    post: vi.fn(() => Promise.resolve({ data: { id: 1 }, error: null })),
    put: vi.fn(() => Promise.resolve({ data: { id: 1 }, error: null })),
    delete: vi.fn(() => Promise.resolve({ data: true, error: null })),
  },
}))

function makeTransaction(overrides: Partial<TransactionResponse> = {}): TransactionResponse {
  return {
    id: 5,
    accountId: null,
    accountName: null,
    date: '2026-07-10',
    description: 'Coffee',
    payee: '',
    memo: '',
    amount: -12.5,
    notes: null,
    isDeleted: false,
    isPending: false,
    isManual: true,
    assignments: [],
    ...overrides,
  }
}

function renderDialog(transaction: TransactionResponse | null) {
  const onClose = vi.fn()
  const onMutate = vi.fn()
  render(
    <Provider store={store}>
      <TransactionEditDialog transaction={transaction} onClose={onClose} onMutate={onMutate} />
    </Provider>,
  )
  return { onClose, onMutate }
}

describe('TransactionEditDialog (Modernist restyle)', () => {
  it('renders a surface-token dialog panel for a new transaction', () => {
    renderDialog(null)
    const heading = screen.getByRole('heading', { name: 'New Transaction' })
    const panel = heading.closest('[class*="bg-surface"]') as HTMLElement
    expect(panel).not.toBeNull()
    expect(panel.className).toContain('border-divider')
    expect(panel.className).not.toContain('rounded')
  })

  it('styles editable fields with the shared .input class', () => {
    renderDialog(null)
    // Description is the first textbox in the form.
    const description = screen.getAllByRole('textbox')[0]
    expect(description.className).toContain('input')
  })

  it('renders Save as a primary button, disabled until there are changes', () => {
    renderDialog(null)
    const save = screen.getByRole('button', { name: 'Save' })
    expect(save.className).toContain('btn')
    expect(save.className).toContain('btn-primary')
    expect(save).toBeDisabled()
  })

  it('enables Save once a valid new transaction is entered', () => {
    renderDialog(null)
    fireEvent.change(screen.getAllByRole('textbox')[0], { target: { value: 'Lunch' } })
    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '20' } })
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('creates a new transaction by posting directly, then mutates and closes', async () => {
    vi.mocked(api.post).mockClear()
    const { onMutate, onClose } = renderDialog(null)

    fireEvent.change(screen.getAllByRole('textbox')[0], { target: { value: 'Lunch' } })
    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '20' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await vi.waitFor(() => expect(onMutate).toHaveBeenCalledTimes(1))
    expect(onClose).toHaveBeenCalledTimes(1)

    const createCall = vi.mocked(api.post).mock.calls.find(([url]) => url === '/api/transactions')
    expect(createCall).toBeDefined()
    const body = createCall![1] as { description: string; amount: number }
    expect(body.description).toBe('Lunch')
    expect(body.amount).toBe(-20)
  })

  it('closes when Cancel is clicked', () => {
    const { onClose } = renderDialog(null)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('does not hijack Enter pressed on a focused button', async () => {
    vi.mocked(api.post).mockClear()
    const { onMutate } = renderDialog(null)

    // A saveable form: Enter anywhere else would save.
    fireEvent.change(screen.getAllByRole('textbox')[0], { target: { value: 'Lunch' } })
    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '20' } })

    const cancel = screen.getByRole('button', { name: 'Cancel' })
    cancel.focus()
    fireEvent.keyDown(cancel, { key: 'Enter' })

    // Enter belongs to the button; the dialog must not save behind its back.
    expect(vi.mocked(api.post)).not.toHaveBeenCalled()
    expect(onMutate).not.toHaveBeenCalled()
  })

  it('re-signs the assignment when flipping an assigned expense to income', async () => {
    vi.mocked(api.put).mockClear()
    const transaction = makeTransaction({
      amount: -3000,
      description: 'Tithe',
      assignments: [{ id: 9, lineItemId: 3, lineItemName: 'Church', amount: -3000 }],
    })
    const { onMutate } = renderDialog(transaction)

    // Flip Expense -> Income; magnitude is unchanged, only the sign flips.
    fireEvent.click(screen.getByRole('radio', { name: 'Income' }))

    const save = screen.getByRole('button', { name: 'Save' })
    expect(save).toBeEnabled()
    fireEvent.click(save)

    await vi.waitFor(() => expect(onMutate).toHaveBeenCalledTimes(1))

    const assignmentCall = vi.mocked(api.put).mock.calls.find(
      ([url]) => typeof url === 'string' && url.endsWith('/assignments'),
    )
    expect(assignmentCall).toBeDefined()
    const body = assignmentCall![1] as { assignments: { lineItemId: number; amount: number }[] }
    expect(body.assignments).toEqual([{ lineItemId: 3, amount: 3000 }])
  })

  it('refuses a third decimal place instead of accepting it and rounding on save', () => {
    renderDialog(null)
    const amount = screen.getByLabelText('Amount')

    fireEvent.change(amount, { target: { value: '10.99' } })
    fireEvent.change(amount, { target: { value: '10.999' } })

    expect(amount).toHaveValue('10.99')
  })

  it('keeps Save disabled for a sub-cent amount, which the field never accepts', () => {
    renderDialog(null)
    fireEvent.change(screen.getAllByRole('textbox')[0], { target: { value: 'Lunch' } })

    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '0.004' } })

    expect(screen.getByLabelText('Amount')).toHaveValue('')
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('sends the entered cents unchanged', async () => {
    vi.mocked(api.post).mockClear()
    const { onMutate } = renderDialog(null)

    fireEvent.change(screen.getAllByRole('textbox')[0], { target: { value: 'Lunch' } })
    fireEvent.change(screen.getByLabelText('Amount'), { target: { value: '10.99' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await vi.waitFor(() => expect(onMutate).toHaveBeenCalledTimes(1))
    const createCall = vi.mocked(api.post).mock.calls.find(([url]) => url === '/api/transactions')
    expect((createCall![1] as { amount: number }).amount).toBe(-10.99)
  })

  it('refuses a third decimal place in a split amount', () => {
    renderDialog(makeTransaction({
      amount: -100,
      assignments: [
        { id: 1, lineItemId: 3, lineItemName: 'Groceries', amount: -60 },
        { id: 2, lineItemId: 4, lineItemName: 'Dining', amount: -40 },
      ],
    }))
    const split = screen.getByLabelText('Groceries amount')

    fireEvent.change(split, { target: { value: '59.99' } })
    fireEvent.change(split, { target: { value: '59.995' } })

    expect(split).toHaveValue('59.99')
  })

  it('sends split amounts that sum exactly to the transaction amount', async () => {
    vi.mocked(api.put).mockClear()
    const { onMutate } = renderDialog(makeTransaction({
      amount: -100,
      assignments: [
        { id: 1, lineItemId: 3, lineItemName: 'Groceries', amount: -60 },
        { id: 2, lineItemId: 4, lineItemName: 'Dining', amount: -40 },
      ],
    }))

    fireEvent.change(screen.getByLabelText('Groceries amount'), { target: { value: '59.99' } })
    fireEvent.change(screen.getByLabelText('Dining amount'), { target: { value: '40.01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await vi.waitFor(() => expect(onMutate).toHaveBeenCalledTimes(1))
    const assignmentCall = vi.mocked(api.put).mock.calls.find(
      ([url]) => typeof url === 'string' && url.endsWith('/assignments'),
    )
    const body = assignmentCall![1] as { assignments: { lineItemId: number; amount: number }[] }
    expect(body.assignments).toEqual([
      { lineItemId: 3, amount: -59.99 },
      { lineItemId: 4, amount: -40.01 },
    ])
  })

  it('offers a destructive Delete action for an unassigned manual transaction', async () => {
    const { onMutate, onClose } = renderDialog(makeTransaction())
    const del = screen.getByRole('button', { name: 'Delete' })
    expect(del.className).toContain('btn-secondary')
    expect(del.className).toContain('text-accent-700')
    fireEvent.click(del)
    // handleDelete awaits the (mocked) API then mutates + closes
    await vi.waitFor(() => expect(onMutate).toHaveBeenCalledTimes(1))
    expect(onClose).toHaveBeenCalledTimes(1)
  })
})
