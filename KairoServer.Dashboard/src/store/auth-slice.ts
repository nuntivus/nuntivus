import { createSlice, type PayloadAction } from '@reduxjs/toolkit'
import {
  clearStoredToken,
  getStoredToken,
  persistToken,
} from '../services/auth/token-storage'

interface AuthState {
  token: string | null
}

const initialState: AuthState = {
  token: getStoredToken(),
}

const authSlice = createSlice({
  name: 'auth',
  initialState,
  reducers: {
    signIn: (state, action: PayloadAction<string>) => {
      const token = action.payload.trim()
      state.token = token
      persistToken(token)
    },
    signOut: (state) => {
      state.token = null
      clearStoredToken()
    },
  },
})

export const { signIn, signOut } = authSlice.actions
export const authReducer = authSlice.reducer
