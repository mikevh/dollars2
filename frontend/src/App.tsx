import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { useAppSelector } from './app/hooks'
import { useTheme } from './features/theme/useTheme'
import LoginPage from './pages/LoginPage'
import BudgetPage from './pages/BudgetPage'
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
              <BudgetPage />
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
