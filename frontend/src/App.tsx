import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { useAppSelector } from './app/hooks'
import LoginPage from './pages/LoginPage'

function App() {
  const { isAuthenticated } = useAppSelector((state) => state.auth)

  return (
    <BrowserRouter>
      <Toaster position="top-right" />
      <Routes>
        <Route
          path="/"
          element={
            isAuthenticated ? (
              <div className="p-4 text-lg">Dollars2</div>
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
    </BrowserRouter>
  )
}

export default App
