import { createSlice, type PayloadAction } from '@reduxjs/toolkit'

interface AppShellState {
  globalError: string | null
}

const initialState: AppShellState = {
  globalError: null,
}

const appShellSlice = createSlice({
  name: 'appShell',
  initialState,
  reducers: {
    setGlobalError: (state, action: PayloadAction<string>) => {
      state.globalError = action.payload
    },
    clearGlobalError: (state) => {
      state.globalError = null
    },
  },
})

export const { setGlobalError, clearGlobalError } = appShellSlice.actions
export const appShellReducer = appShellSlice.reducer
