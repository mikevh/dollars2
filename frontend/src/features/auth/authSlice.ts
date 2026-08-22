import { createSlice, createAsyncThunk } from '@reduxjs/toolkit'
import { api } from '../../api/client'
import { performPasskeyAssertion, type PasskeyOptionsResponse } from './webauthn'

interface AuthResponse {
  token: string
  refreshToken: string
  expiresAt: string
}

interface AuthState {
  token: string | null
  isAuthenticated: boolean
  loading: boolean
  error: string | null
}

const initialState: AuthState = {
  token: localStorage.getItem('token'),
  isAuthenticated: !!localStorage.getItem('token'),
  loading: false,
  error: null,
}

export const loginThunkAsync = createAsyncThunk('auth/login',
  async (email: string, { rejectWithValue }) => {
    // Passkeys are scoped to whatever rp.id the backend was reachable at when they were
    // registered, which makes them awkward for local debugging against the real prod database
    // (see /api/auth/dev/login's backend-side comment for why this is safe to skip in dev only).
    if (import.meta.env.DEV) {
      const devResult = await api.post<AuthResponse>('/api/auth/dev/login', { email })
      if (devResult.error) {
        return rejectWithValue(devResult.error.message)
      }
      return devResult.data!
    }

    const optionsResult = await api.post<PasskeyOptionsResponse>('/api/auth/passkey/login/options', { email })
    if (optionsResult.error) {
      return rejectWithValue(optionsResult.error.message)
    }

    let credentialJson: string
    try {
      credentialJson = await performPasskeyAssertion(optionsResult.data!.optionsJson)
    } catch (err) {
      return rejectWithValue(err instanceof Error ? err.message : 'Passkey sign-in failed.')
    }

    const result = await api.post<AuthResponse>('/api/auth/passkey/login/complete', { credentialJson })
    if (result.error) {
      return rejectWithValue(result.error.message)
    }
    return result.data!
  }
)

const authSlice = createSlice({
  name: 'auth',
  initialState,
  reducers: {
    logout(state) {
      state.token = null
      state.isAuthenticated = false
      state.error = null
      localStorage.removeItem('token')
      localStorage.removeItem('refreshToken')
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(loginThunkAsync.pending, (state) => {
        state.loading = true
        state.error = null
      })
      .addCase(loginThunkAsync.fulfilled, (state, action) => {
        state.loading = false
        state.token = action.payload.token
        state.isAuthenticated = true
        localStorage.setItem('token', action.payload.token)
        localStorage.setItem('refreshToken', action.payload.refreshToken)
      })
      .addCase(loginThunkAsync.rejected, (state, action) => {
        state.loading = false
        state.error = action.payload as string
      })
  },
})

export const { logout } = authSlice.actions
export default authSlice.reducer
