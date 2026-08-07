import { configureStore } from '@reduxjs/toolkit'
import authReducer from '../features/auth/authSlice'
import themeReducer from '../features/theme/themeSlice'
import budgetReducer from '../features/budget/budgetSlice'
import transactionReducer from '../features/transactions/transactionSlice'
import rawHistoryReducer from '../features/transactions/rawHistorySlice'
import accountsReducer from '../features/accounts/accountsSlice'
import accountTransactionsReducer from '../features/accountTransactions/accountTransactionsSlice'
import syncArchiveReducer from '../features/syncArchive/syncArchiveSlice'

export const store = configureStore({
  reducer: {
    auth: authReducer,
    theme: themeReducer,
    budget: budgetReducer,
    transactions: transactionReducer,
    rawHistory: rawHistoryReducer,
    accounts: accountsReducer,
    accountTransactions: accountTransactionsReducer,
    syncArchive: syncArchiveReducer,
  },
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
