import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { useAppSelector } from './app/hooks'
import { useTheme } from './features/theme/useTheme'
import LoginPage from './pages/LoginPage'
import Footer from './components/Footer'

function App() {
  const { isAuthenticated } = useAppSelector((state) => state.auth)
  useTheme()

  return (
    <BrowserRouter>
      <Toaster position="top-right" />
      <Routes>
        <Route
          path="/"
          element={
            isAuthenticated ? (
              <div className="min-h-screen bg-gray-50 pb-12 dark:bg-gray-900">
                <div className="p-4 text-lg text-gray-900 dark:text-white">Dollars2</div>
              </div>
            ) : (
              <Navigate to="/login" replace />
            )
          }
        />
        <Route
          path="/login"
          element={
            isAuthenticated ? (
              <Navigate to="/" replace />
            ) : (
              <LoginPage />
            )
          }
        />
      </Routes>
      {isAuthenticated && <Footer />}
    </BrowserRouter>
  )
}

export default App
