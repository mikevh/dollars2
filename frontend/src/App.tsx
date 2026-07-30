import { BrowserRouter, Routes, Route, Navigate, Outlet } from 'react-router-dom'
import { Toaster } from 'react-hot-toast'
import { useAppSelector } from './app/hooks'
import { useTheme } from './features/theme/useTheme'
import LoginPage from './pages/LoginPage'
import BudgetPage from './pages/BudgetPage'
import AccountsPage from './pages/AccountsPage'
import AccountTransactionsPage from './pages/AccountTransactionsPage'
import Footer from './components/Footer'

function App() {
  const { isAuthenticated } = useAppSelector((state) => state.auth);
  useTheme();

  return (
    <BrowserRouter>
      <Toaster position="top-right" />
      <Routes>
        <Route element={isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />}>
          <Route path="/" element={<BudgetPage />} />
          <Route path="/accounts" element={<AccountsPage />} />
          <Route path="/accounts/:accountId" element={<AccountTransactionsPage />} />
        </Route>
        <Route path="/login" element={ isAuthenticated ? <Navigate to="/" replace /> : <LoginPage /> } />
      </Routes>
      { isAuthenticated && <Footer /> }
    </BrowserRouter>
  )
}

export default App
