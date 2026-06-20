import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import type { TransactionResponse } from '../../types/transaction'

type TransactionTab = 'new' | 'tracked' | 'deleted' | 'pending'

interface TransactionState {
  transactions: TransactionResponse[]
  loading: boolean
  error: string | null
  activeTab: TransactionTab
}

const initialState: TransactionState = {
  transactions: [],
  loading: false,
  error: null,
  activeTab: 'new',
}

export const fetchNewTransactions = createAsyncThunk(
  'transactions/fetchNew',
  async (_, { rejectWithValue }) => {
    const result = await api.get<TransactionResponse[]>('/api/transactions/new')
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const fetchTrackedTransactions = createAsyncThunk(
  'transactions/fetchTracked',
  async ({ fromDate }: { fromDate: string }, { rejectWithValue }) => {
    const result = await api.get<TransactionResponse[]>(`/api/transactions/tracked?fromDate=${encodeURIComponent(fromDate)}`)
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const fetchDeletedTransactions = createAsyncThunk(
  'transactions/fetchDeleted',
  async (_, { rejectWithValue }) => {
    const result = await api.get<TransactionResponse[]>('/api/transactions/deleted')
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const fetchPendingTransactions = createAsyncThunk(
  'transactions/fetchPending',
  async (_, { rejectWithValue }) => {
    const result = await api.get<TransactionResponse[]>('/api/transactions/pending')
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const createTransaction = createAsyncThunk(
  'transactions/create',
  async ({ date, description, amount, notes }: { date: string; description: string; amount: number; notes?: string }, { rejectWithValue }) => {
    const result = await api.post<TransactionResponse>('/api/transactions', { date, description, amount, notes })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

export const softDeleteTransaction = createAsyncThunk(
  'transactions/softDelete',
  async ({ id }: { id: number }, { rejectWithValue }) => {
    const result = await api.delete<boolean>(`/api/transactions/${id}`)
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return id
  }
)

export const restoreTransaction = createAsyncThunk(
  'transactions/restore',
  async ({ id }: { id: number }, { rejectWithValue }) => {
    const result = await api.post<boolean>(`/api/transactions/${id}/restore`)
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return id
  }
)

export const hardDeleteTransaction = createAsyncThunk(
  'transactions/hardDelete',
  async ({ id }: { id: number }, { rejectWithValue }) => {
    const result = await api.delete<boolean>(`/api/transactions/${id}/permanent`)
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return id
  }
)

export const assignTransaction = createAsyncThunk(
  'transactions/assign',
  async ({ id, lineItemId }: { id: number; lineItemId: number }, { rejectWithValue }) => {
    const result = await api.post<TransactionResponse>(`/api/transactions/${id}/assign`, { lineItemId })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return id
  }
)

const pending = (state: TransactionState) => {
  state.loading = true
  state.error = null
}

const fulfilled = (state: TransactionState, action: { payload: TransactionResponse[] }) => {
  state.loading = false
  state.transactions = action.payload
}

const rejected = (state: TransactionState, action: { payload: unknown }) => {
  state.loading = false
  state.error = action.payload as string
}

const transactionSlice = createSlice({
  name: 'transactions',
  initialState,
  reducers: {
    setActiveTab(state, action: { payload: TransactionTab }) {
      state.activeTab = action.payload
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(fetchNewTransactions.pending, pending)
      .addCase(fetchNewTransactions.fulfilled, fulfilled)
      .addCase(fetchNewTransactions.rejected, rejected)
      .addCase(fetchTrackedTransactions.pending, pending)
      .addCase(fetchTrackedTransactions.fulfilled, fulfilled)
      .addCase(fetchTrackedTransactions.rejected, rejected)
      .addCase(fetchDeletedTransactions.pending, pending)
      .addCase(fetchDeletedTransactions.fulfilled, fulfilled)
      .addCase(fetchDeletedTransactions.rejected, rejected)
      .addCase(fetchPendingTransactions.pending, pending)
      .addCase(fetchPendingTransactions.fulfilled, fulfilled)
      .addCase(fetchPendingTransactions.rejected, rejected)
      .addCase(createTransaction.fulfilled, (state, action) => {
        state.transactions.unshift(action.payload)
      })
      .addCase(softDeleteTransaction.fulfilled, (state, action) => {
        state.transactions = state.transactions.filter((t) => t.id !== action.payload)
      })
      .addCase(restoreTransaction.fulfilled, (state, action) => {
        state.transactions = state.transactions.filter((t) => t.id !== action.payload)
      })
      .addCase(hardDeleteTransaction.fulfilled, (state, action) => {
        state.transactions = state.transactions.filter((t) => t.id !== action.payload)
      })
      .addCase(assignTransaction.fulfilled, (state, action) => {
        state.transactions = state.transactions.filter((t) => t.id !== action.payload)
      })
  },
})

export const { setActiveTab } = transactionSlice.actions
export default transactionSlice.reducer
