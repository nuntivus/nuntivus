import { configureStore } from '@reduxjs/toolkit'
import { appShellReducer } from './app-shell-slice'
import { authReducer } from './auth-slice'
import { uiReducer } from './ui-slice'

export const store = configureStore({
  reducer: {
    appShell: appShellReducer,
    auth: authReducer,
    ui: uiReducer,
  },
})

export type RootState = ReturnType<typeof store.getState>
export type AppDispatch = typeof store.dispatch
