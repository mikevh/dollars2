import { useEffect } from 'react'
import { useAppSelector } from '../../app/hooks'

// Authority for the theme class once React is mounted: runtime switching and
// following the OS in 'system' mode. The first paint is handled earlier by the
// inline script in index.html, which duplicates the isDark decision below —
// change one, change both, or dark-mode users get a light flash on load.
export function useTheme() {
  const mode = useAppSelector((state) => state.theme.mode)

  useEffect(() => {
    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)')

    const applyTheme = () => {
      const isDark =
        mode === 'dark' || (mode === 'system' && mediaQuery.matches)

      if (isDark) {
        document.documentElement.classList.add('dark')
      } else {
        document.documentElement.classList.remove('dark')
      }
    }

    applyTheme()
    mediaQuery.addEventListener('change', applyTheme)
    return () => mediaQuery.removeEventListener('change', applyTheme)
  }, [mode])
}
