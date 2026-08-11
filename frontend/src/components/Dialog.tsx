import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import type { KeyboardEvent, ReactNode, RefObject } from 'react'

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

interface DialogProps {
  onClose: () => void
  /** Id of the heading that names this dialog — see DialogHeader. */
  labelledBy: string
  /**
   * Control to focus on open. Defaults to the panel itself, which is the right
   * target for dialogs that are just a prompt. Pass a ref rather than putting
   * autoFocus on the control: Dialog focuses on every mount, and a native
   * autoFocus fires only on the first one.
   */
  initialFocusRef?: RefObject<HTMLElement | null>
  /** Panel width utility, e.g. "max-w-sm". Everything else about the panel is shared. */
  className?: string
  children: ReactNode
}

/**
 * Modal shell: backdrop, panel chrome, and the keyboard/focus behaviour every
 * dialog needs. Visibility is fully controlled by the caller — render it to open,
 * unmount it to close.
 */
export default function Dialog({ onClose, labelledBy, initialFocusRef, className = 'max-w-md', children }: DialogProps) {
  const panelRef = useRef<HTMLDivElement>(null)

  // The document listener is attached once on mount and reads the current onClose
  // through a ref, so an identity change on the caller's callback can't leave a
  // stale closure subscribed.
  const latestClose = useRef(onClose)
  useEffect(() => {
    latestClose.current = onClose
  })

  useEffect(() => {
    const handleKeyDown = (e: globalThis.KeyboardEvent) => {
      if (e.key === 'Escape') {
        latestClose.current()
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [])

  // Captured during render rather than in the effect below, which runs after focus
  // has already moved into the dialog — reading document.activeElement there would
  // record a child as the opener and later "restore" focus to it.
  const [opener] = useState(() => document.activeElement as HTMLElement | null)

  // Focus moves into the dialog on open and back to the opener on close. Both halves
  // are symmetric so the effect is safe to tear down and re-run: StrictMode does
  // exactly that in dev, and anything that only worked on the first mount (a child's
  // native autoFocus, say) would be left behind by the remount. Runs before paint,
  // matching the timing of the autoFocus this replaces, so keystrokes that land
  // immediately after a slow open aren't dropped on the trigger.
  useLayoutEffect(() => {
    const target = initialFocusRef?.current ?? panelRef.current
    target?.focus()
    return () => {
      if (opener?.isConnected) {
        opener.focus()
      }
    }
  }, [opener, initialFocusRef])

  const handleTabTrap = (e: KeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'Tab' || !panelRef.current) {
      return
    }
    const focusable = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
    if (focusable.length === 0) {
      e.preventDefault()
      return
    }
    const first = focusable[0]
    const last = focusable[focusable.length - 1]
    const active = document.activeElement
    if (e.shiftKey && (active === first || active === panelRef.current)) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && active === last) {
      e.preventDefault()
      first.focus()
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4" onClick={onClose}>
      <div className="fixed inset-0 bg-black/50" />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={labelledBy}
        tabIndex={-1}
        onClick={(e) => e.stopPropagation()}
        onKeyDown={handleTabTrap}
        className={`relative w-full ${className} max-h-[calc(100vh-160px)] overflow-y-auto rounded-[var(--radius-card)] bg-[var(--app-card)] pt-[22px] px-6 pb-5 text-text shadow-[var(--app-shadow-lg)] focus:outline-none`}
      >
        {children}
      </div>
    </div>
  )
}

/** Standard heading row: the dialog's title plus its close button. */
export function DialogHeader({ id, title, onClose }: { id: string; title: ReactNode; onClose: () => void }) {
  return (
    <div className="mb-4 flex items-center justify-between">
      <h2 id={id} className="font-heading text-lg font-extrabold uppercase tracking-wide text-text">
        {title}
      </h2>
      <button onClick={onClose} aria-label="Close" className="text-muted hover:text-accent-700">
        <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5">
          <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
        </svg>
      </button>
    </div>
  )
}
