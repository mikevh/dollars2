import { useEffect } from 'react'
import { useAppSelector } from '../../app/hooks'

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
