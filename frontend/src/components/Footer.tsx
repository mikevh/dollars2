import { useNavigate, useLocation, Link } from 'react-router-dom'
import toast from 'react-hot-toast'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faSun, faMoon, faDesktop, faSync, faSignOutAlt, faBuildingColumns, faList, faChartLine } from '@fortawesome/free-solid-svg-icons'
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
  const location = useLocation()
  const mode = useAppSelector((state) => state.theme.mode)

  const onAccounts = location.pathname === '/accounts'

  const buildId = import.meta.env.VITE_BUILD_ID || 'dev'
  const buildDate = import.meta.env.VITE_BUILD_DATE
  const buildLabel = buildDate ? `${buildId} · ${buildDate}` : buildId

  const kibanaUrl = import.meta.env.VITE_KIBANA_URL

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
    <footer className="fixed bottom-0 left-0 right-0 flex items-center justify-between border-t border-divider bg-[var(--app-card)] px-5 py-2 text-[14px] text-text">
      <div className="flex items-center gap-2">
        <button
          onClick={cycleTheme}
          className="btn btn-icon btn-ghost"
          title={`Theme: ${mode}`}
        >
          <FontAwesomeIcon icon={themeIcons[mode]} className="h-[15px] w-[15px]" />
        </button>
        <span className="opacity-50" title="Running build">
          {buildLabel}
        </span>
        {kibanaUrl ? (
          <a
            href={kibanaUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="btn btn-ghost text-[14px]"
            title="Logs"
          >
            <FontAwesomeIcon icon={faChartLine} className="h-[15px] w-[15px]" />
            <span>Logs</span>
          </a>
        ) : null}
      </div>

      <div className="flex items-center gap-1.5 opacity-50">
        <FontAwesomeIcon icon={faSync} className="h-3 w-3" />
        <span>Last synced: —</span>
      </div>

      <div className="flex items-center gap-1">
        <Link
          to={onAccounts ? '/' : '/accounts'}
          className="btn btn-ghost text-[14px]"
          title={onAccounts ? 'Budget' : 'Accounts'}
        >
          <FontAwesomeIcon icon={onAccounts ? faList : faBuildingColumns} className="h-[15px] w-[15px]" />
          <span>{onAccounts ? 'Budget' : 'Accounts'}</span>
        </Link>
        <button
          onClick={handleLogout}
          className="btn btn-icon btn-ghost"
          title="Logout"
        >
          <FontAwesomeIcon icon={faSignOutAlt} className="h-[15px] w-[15px]" />
        </button>
      </div>
    </footer>
  )
}
