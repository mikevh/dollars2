import { StrictMode, useRef } from 'react'
import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import Dialog, { DialogHeader } from './Dialog'

function renderDialog(onClose = vi.fn(), children?: React.ReactNode) {
  const result = render(
    <Dialog onClose={onClose} labelledBy="title">
      <DialogHeader id="title" title="Test Dialog" onClose={onClose} />
      {children ?? (
        <>
          <input aria-label="First" />
          <button>Last</button>
        </>
      )}
    </Dialog>
  )
  return { ...result, onClose }
}

function DialogWithInitialFocus({ onClose = vi.fn() }: { onClose?: () => void }) {
  const inputRef = useRef<HTMLInputElement>(null)
  return (
    <Dialog onClose={onClose} labelledBy="title" initialFocusRef={inputRef}>
      <DialogHeader id="title" title="Test Dialog" onClose={onClose} />
      <input ref={inputRef} aria-label="Named" />
    </Dialog>
  )
}

describe('Dialog', () => {
  it('exposes dialog semantics labelled by its heading', () => {
    renderDialog()

    const panel = screen.getByRole('dialog')
    expect(panel).toHaveAttribute('aria-modal', 'true')
    expect(panel).toHaveAttribute('aria-labelledby', 'title')
    expect(screen.getByRole('heading', { name: 'Test Dialog' })).toHaveAttribute('id', 'title')
  })

  it('closes on Escape', () => {
    const { onClose } = renderDialog()

    fireEvent.keyDown(document, { key: 'Escape' })

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('closes on backdrop click but not on a click inside the panel', () => {
    const { onClose } = renderDialog()

    fireEvent.click(screen.getByRole('dialog'))
    expect(onClose).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('dialog').parentElement!)
    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('closes from the header close button', () => {
    const { onClose } = renderDialog()

    fireEvent.click(screen.getByRole('button', { name: 'Close' }))

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('focuses the panel on open when no initial focus is named', () => {
    renderDialog()

    expect(screen.getByRole('dialog')).toHaveFocus()
  })

  it('focuses the control named by initialFocusRef on open', () => {
    render(<DialogWithInitialFocus />)

    expect(screen.getByRole('textbox', { name: 'Named' })).toHaveFocus()
  })

  it('keeps the named control focused across a StrictMode effect remount', () => {
    // StrictMode tears an effect down and re-runs it on mount. Focus setup that only
    // works once — a native autoFocus, say — does not survive that; Dialog's must.
    render(
      <StrictMode>
        <DialogWithInitialFocus />
      </StrictMode>
    )

    expect(screen.getByRole('textbox', { name: 'Named' })).toHaveFocus()
  })

  it('restores focus to the opener on close', () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()

    const { unmount } = renderDialog()
    expect(trigger).not.toHaveFocus()

    unmount()
    expect(trigger).toHaveFocus()

    trigger.remove()
  })

  it('restores focus to the opener when the dialog focused a named control', () => {
    const trigger = document.createElement('button')
    document.body.appendChild(trigger)
    trigger.focus()

    const { unmount } = render(<DialogWithInitialFocus />)
    expect(screen.getByRole('textbox', { name: 'Named' })).toHaveFocus()

    unmount()
    expect(trigger).toHaveFocus()

    trigger.remove()
  })

  it('wraps Tab from the last focusable back to the first', () => {
    renderDialog()

    const last = screen.getByRole('button', { name: 'Last' })
    last.focus()
    fireEvent.keyDown(last, { key: 'Tab' })

    expect(screen.getByRole('button', { name: 'Close' })).toHaveFocus()
  })

  it('wraps Shift-Tab from the first focusable back to the last', () => {
    renderDialog()

    const first = screen.getByRole('button', { name: 'Close' })
    first.focus()
    fireEvent.keyDown(first, { key: 'Tab', shiftKey: true })

    expect(screen.getByRole('button', { name: 'Last' })).toHaveFocus()
  })

  it('wraps Shift-Tab from the panel itself back to the last focusable', () => {
    renderDialog()

    const panel = screen.getByRole('dialog')
    expect(panel).toHaveFocus()
    fireEvent.keyDown(panel, { key: 'Tab', shiftKey: true })

    expect(screen.getByRole('button', { name: 'Last' })).toHaveFocus()
  })

  it('leaves Tab alone in the middle of the panel', () => {
    renderDialog()

    const middle = screen.getByRole('textbox', { name: 'First' })
    middle.focus()
    fireEvent.keyDown(middle, { key: 'Tab' })

    // No wrap — the browser's own sequential navigation handles this case.
    expect(middle).toHaveFocus()
  })
})
