import { render, screen, fireEvent } from '@testing-library/react'
import { Provider } from 'react-redux'
import { describe, expect, it, vi } from 'vitest'
import { store } from '../../app/store'
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
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '20' } })
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('closes when Cancel is clicked', () => {
    const { onClose } = renderDialog(null)
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    expect(onClose).toHaveBeenCalledTimes(1)
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
