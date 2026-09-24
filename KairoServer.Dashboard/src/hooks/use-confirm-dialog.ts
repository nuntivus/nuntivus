import { createContext, useContext } from 'react'

export interface ConfirmOptions {
  title: string
  description: string
  confirmText?: string
  cancelText?: string
  confirmColor?:
    | 'red'
    | 'indigo'
    | 'gray'
    | 'amber'
    | 'green'
    | 'blue'
    | 'cyan'
    | 'crimson'
    | 'gold'
    | 'grass'
    | 'iris'
    | 'jade'
    | 'lime'
    | 'mint'
    | 'orange'
    | 'pink'
    | 'plum'
    | 'purple'
    | 'ruby'
    | 'sky'
    | 'teal'
    | 'tomato'
    | 'violet'
    | 'yellow'
}

export type ConfirmHandler = (options: ConfirmOptions) => Promise<boolean>

export const ConfirmDialogContext = createContext<ConfirmHandler | null>(null)

export function useConfirmDialog() {
  const context = useContext(ConfirmDialogContext)

  if (!context) {
    throw new Error('useConfirmDialog must be used inside ConfirmDialogProvider')
  }

  return context
}
