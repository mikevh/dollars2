import { useNavigate } from 'react-router-dom'
import toast from 'react-hot-toast'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faSun, faMoon, faDesktop, faSync, faSignOutAlt } from '@fortawesome/free-solid-svg-icons'
import { useAppDispatch, useAppSelector } from '../app/hooks'
import { setTheme, type ThemeMode } from '../features/theme/themeSlice'
import { logout } from '../features/auth/authSlice'

const themeOrder: ThemeMode[] = ['light', 'dark', 'system']
const themeIcons = {
  light: faSun,
  dark: faMoon,
  system: faDesktop,
}

export default function Footer() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const mode = useAppSelector((state) => state.theme.mode)

  const buildId = import.meta.env.VITE_BUILD_ID || 'dev'
  const buildDate = import.meta.env.VITE_BUILD_DATE
  const buildLabel = buildDate ? `${buildId} · ${buildDate}` : buildId

  const cycleTheme = () => {
    const currentIndex = themeOrder.indexOf(mode)
    const next = themeOrder[(currentIndex + 1) % themeOrder.length]
    dispatch(setTheme(next))
    toast.success(`Theme set to ${next}`)
  }

  const handleLogout = () => {
    dispatch(logout())
    navigate('/login')
  }

  return (
    <footer className="fixed bottom-0 left-0 right-0 flex items-center justify-between border-t border-gray-200 bg-white px-4 py-2 text-sm text-gray-600 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-400">
      <div className="flex items-center gap-3">
        <button
          onClick={cycleTheme}
          className="rounded p-1 hover:text-gray-900 dark:hover:text-white"
          title={`Theme: ${mode}`}
        >
          <FontAwesomeIcon icon={themeIcons[mode]} className="h-4 w-4" />
        </button>
        <span className="text-xs opacity-50" title="Running build">
          {buildLabel}
        </span>
      </div>

      <div className="flex items-center gap-2">
        <FontAwesomeIcon icon={faSync} className="h-3 w-3 opacity-50" />
        <span className="opacity-50">Last synced: —</span>
      </div>

      <div className="flex items-center gap-3">
        <button
          onClick={handleLogout}
          className="rounded p-1 hover:text-gray-900 dark:hover:text-white"
          title="Logout"
        >
          <FontAwesomeIcon icon={faSignOutAlt} className="h-4 w-4" />
        </button>
      </div>
    </footer>
  )
}
