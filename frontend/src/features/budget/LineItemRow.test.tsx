import { fireEvent, render, screen } from '@testing-library/react'
import { Provider } from 'react-redux'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../api/client'
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
  const { rerender } = render(
    <Provider store={store}>
      <LineItemRow lineItem={makeLineItem()} groupId={1} isIncome={false} {...props} />
    </Provider>,
  )
  return {
    // Re-render in place — the point of these tests is a prop change with no remount.
    setLineItem: (lineItem: LineItemResponse) =>
      rerender(
        <Provider store={store}>
          <LineItemRow lineItem={lineItem} groupId={1} isIncome={false} {...props} />
        </Provider>,
      ),
  }
}

beforeEach(() => {
  vi.mocked(api.put).mockClear()
})

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

describe('LineItemRow draft re-sync', () => {
  it('seeds the amount editor from the latest prop after an in-place update', () => {
    const { setLineItem } = renderRow()
    setLineItem(makeLineItem({ plannedAmount: 400 }))

    fireEvent.click(screen.getByText('$400.00'))
    expect(screen.getByRole('spinbutton')).toHaveValue(400)
  })

  it('seeds the name editor from the latest prop after an in-place rename', () => {
    const { setLineItem } = renderRow()
    setLineItem(makeLineItem({ name: 'Mortgage' }))

    fireEvent.click(screen.getByText('Mortgage'))
    expect(screen.getByRole('textbox')).toHaveValue('Mortgage')
  })

  it('issues no update when an editor is opened and blurred without a change', async () => {
    const { setLineItem } = renderRow()
    setLineItem(makeLineItem({ plannedAmount: 400 }))

    const span = screen.getByText('$400.00')
    fireEvent.click(span)
    fireEvent.blur(screen.getByRole('spinbutton'))

    await vi.waitFor(() => expect(screen.getByText('$400.00')).toBeInTheDocument())
    expect(vi.mocked(api.put)).not.toHaveBeenCalled()
  })

  it('leaves an open editor alone when the prop changes underneath it', () => {
    const { setLineItem } = renderRow()

    fireEvent.click(screen.getByText('$200.00'))
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '999' } })
    setLineItem(makeLineItem({ plannedAmount: 400 }))

    expect(screen.getByRole('spinbutton')).toHaveValue(999)
  })

  // A rejected save leaves the prop unchanged, so the re-seed can't fire on its own.
  it('drops a rejected amount draft instead of re-submitting it on the next blur', async () => {
    vi.mocked(api.put).mockResolvedValueOnce({
      data: null,
      error: { message: 'nope', code: 'SERVER_ERROR' },
    })
    renderRow()

    fireEvent.click(screen.getByText('$200.00'))
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value: '999' } })
    fireEvent.blur(screen.getByRole('spinbutton'))
    await vi.waitFor(() => expect(screen.getByText('$200.00')).toBeInTheDocument())

    vi.mocked(api.put).mockClear()
    fireEvent.click(screen.getByText('$200.00'))
    expect(screen.getByRole('spinbutton')).toHaveValue(200)

    fireEvent.blur(screen.getByRole('spinbutton'))
    await vi.waitFor(() => expect(screen.getByText('$200.00')).toBeInTheDocument())
    expect(vi.mocked(api.put)).not.toHaveBeenCalled()
  })

  it('drops a rejected name draft instead of re-submitting it on the next blur', async () => {
    vi.mocked(api.put).mockResolvedValueOnce({
      data: null,
      error: { message: 'nope', code: 'SERVER_ERROR' },
    })
    renderRow()

    fireEvent.click(screen.getByText('Rent'))
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'Mortgage' } })
    fireEvent.blur(screen.getByRole('textbox'))
    await vi.waitFor(() => expect(screen.getByText('Rent')).toBeInTheDocument())

    vi.mocked(api.put).mockClear()
    fireEvent.click(screen.getByText('Rent'))
    expect(screen.getByRole('textbox')).toHaveValue('Rent')

    fireEvent.blur(screen.getByRole('textbox'))
    await vi.waitFor(() => expect(screen.getByText('Rent')).toBeInTheDocument())
    expect(vi.mocked(api.put)).not.toHaveBeenCalled()
  })
})
