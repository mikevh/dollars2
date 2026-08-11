import { useEffect, useRef } from 'react'

/**
 * Focuses and selects an editor's text every time `open` flips to true. `autoFocus` alone isn't
 * enough here: the controlled `value` write on mount collapses any selection, so focus+select has
 * to happen after that write settles — a `setTimeout(…, 0)` (not `requestAnimationFrame`, which
 * doesn't fire reliably in a background tab).
 */
export function useFocusSelectOnOpen<T extends HTMLInputElement>(open: boolean) {
  const ref = useRef<T>(null)

  useEffect(() => {
    if (!open) {
      return
    }
    const id = setTimeout(() => {
      ref.current?.focus()
      ref.current?.select()
    }, 0)
    return () => clearTimeout(id)
  }, [open])

  return ref
}
