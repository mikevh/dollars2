import { configureStore } from '@reduxjs/toolkit'
import authReducer from '../features/auth/authSlice'
import themeReducer from '../features/theme/themeSlice'
import budgetReducer from '../features/budget/budgetSlice'
import transactionReducer from '../features/transactions/transactionSlice'
import accountsReducer from '../features/accounts/accountsSlice'
import accountTransactionsReducer from '../features/accountTransactions/accountTransactionsSlice'

export const store = configureStore({
  reducer: {
    auth: authReducer,
    theme: themeReducer,
    budget: budgetReducer,
    transactions: transactionReducer,
    accounts: accountsReducer,
    accountTransactions: accountTransactionsReducer,
  },
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
