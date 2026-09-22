import { createAsyncThunk, createSlice, type PayloadAction } from '@reduxjs/toolkit'
import {
  clearStoredToken,
  getStoredToken,
  persistToken,
} from '../services/auth/token-storage'
import { getCurrentUser, type CurrentUser } from '../services/api/auth-api'

interface AuthState {
  token: string | null
  currentUser: CurrentUser | null
  status: 'idle' | 'loading' | 'authenticated' | 'unauthenticated'
  error: string | null
}

const initialState: AuthState = {
  token: getStoredToken(),
  currentUser: null,
  status: getStoredToken() ? 'loading' : 'unauthenticated',
  error: null,
}

export const loadCurrentUser = createAsyncThunk<CurrentUser, string>(
  'auth/loadCurrentUser',
  async (token, thunkApi) => {
    try {
      return await getCurrentUser(token)
    } catch {
      return thunkApi.rejectWithValue('Unable to load current user') as never
    }
  },
)

export const loginWithToken = createAsyncThunk<CurrentUser, string>(
  'auth/loginWithToken',
  async (token, thunkApi) => {
    const normalizedToken = token.trim()

    if (!normalizedToken) {
      return thunkApi.rejectWithValue('Token is required') as never
    }

    persistToken(normalizedToken)

    try {
      return await getCurrentUser(normalizedToken)
    } catch {
      clearStoredToken()
      return thunkApi.rejectWithValue('Authentication failed') as never
    }
  },
)

const authSlice = createSlice({
  name: 'auth',
  initialState,
  reducers: {
    signIn: (state, action: PayloadAction<string>) => {
      const token = action.payload.trim()
      state.token = token
      persistToken(token)
      state.status = 'authenticated'
      state.error = null
    },
    signOut: (state) => {
      state.token = null
      state.currentUser = null
      state.status = 'unauthenticated'
      state.error = null
      clearStoredToken()
    },
    clearAuthError: (state) => {
      state.error = null
    },
    markSessionExpired: (state) => {
      state.token = null
      state.currentUser = null
      state.status = 'unauthenticated'
      state.error = 'Session expired. Please sign in again.'
      clearStoredToken()
    },
  },
  extraReducers: (builder) => {
    builder
      .addCase(loadCurrentUser.pending, (state) => {
        state.status = 'loading'
        state.error = null
      })
      .addCase(loadCurrentUser.fulfilled, (state, action) => {
        state.currentUser = action.payload
        state.status = 'authenticated'
        state.error = null
      })
      .addCase(loadCurrentUser.rejected, (state) => {
        state.token = null
        state.currentUser = null
        state.status = 'unauthenticated'
        state.error = 'Your session is invalid. Please sign in again.'
        clearStoredToken()
      })
      .addCase(loginWithToken.pending, (state) => {
        state.status = 'loading'
        state.error = null
      })
      .addCase(loginWithToken.fulfilled, (state, action) => {
        state.currentUser = action.payload
        state.token = action.meta.arg.trim()
        state.status = 'authenticated'
        state.error = null
      })
      .addCase(loginWithToken.rejected, (state, action) => {
        state.token = null
        state.currentUser = null
        state.status = 'unauthenticated'
        state.error =
          typeof action.payload === 'string'
            ? action.payload
            : 'Authentication failed'
      })
  },
})

export const { clearAuthError, markSessionExpired, signIn, signOut } = authSlice.actions
export const authReducer = authSlice.reducer
