import { useEffect, useRef, type RefObject } from 'react'

/**
 * Fires `onOutside` on any mousedown outside `containerRef`, while `active`. The callback is read
 * through a ref (mirrors Dialog.tsx's pattern for its own document-level listener) so the effect
 * doesn't need `onOutside` as a dependency and isn't torn down/rebuilt on every render.
 */
export function useOutsideMousedown(containerRef: RefObject<HTMLElement | null>, active: boolean, onOutside: () => void) {
  const onOutsideRef = useRef(onOutside)

  useEffect(() => {
    onOutsideRef.current = onOutside
  }, [onOutside])

  useEffect(() => {
    if (!active) {
      return
    }
    const handleMouseDown = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        onOutsideRef.current()
      }
    }
    document.addEventListener('mousedown', handleMouseDown)
    return () => document.removeEventListener('mousedown', handleMouseDown)
  }, [active, containerRef])
}
