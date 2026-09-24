import { createAsyncThunk, createSlice, type PayloadAction } from '@reduxjs/toolkit'
import {
  clearStoredToken,
  getStoredToken,
  persistToken,
} from '../services/auth/token-storage'
import { getCurrentUser, loginWithPassword, type CurrentUser } from '../services/api/auth-api'

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

export const loginWithCredentials = createAsyncThunk<CurrentUser, { username: string; password: string }>(
  'auth/loginWithCredentials',
  async ({ username, password }, thunkApi) => {
    const normalizedUsername = username.trim()
    const normalizedPassword = password.trim()

    if (!normalizedUsername || !normalizedPassword) {
      return thunkApi.rejectWithValue('Username and password are required') as never
    }

    try {
      const login = await loginWithPassword({
        username: normalizedUsername,
        password: normalizedPassword,
      })

      persistToken(login.accessToken)
      return await getCurrentUser(login.accessToken)
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
      .addCase(loginWithCredentials.pending, (state) => {
        state.status = 'loading'
        state.error = null
      })
      .addCase(loginWithCredentials.fulfilled, (state, action) => {
        state.currentUser = action.payload
        state.token = getStoredToken()
        state.status = 'authenticated'
        state.error = null
      })
      .addCase(loginWithCredentials.rejected, (state, action) => {
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
