import { useEffect } from 'react'
import { useBlocker } from 'react-router-dom'
import { useConfirmDialog } from './use-confirm-dialog'

interface UnsavedGuardOptions {
  when: boolean
  title?: string
  description?: string
  confirmText?: string
  cancelText?: string
}

export function useUnsavedChangesGuard({
  when,
  title = 'Unsaved changes',
  description = 'You have unsaved changes. Leave this page?',
  confirmText = 'Leave page',
  cancelText = 'Stay',
}: UnsavedGuardOptions) {
  const blocker = useBlocker(when)
  const confirm = useConfirmDialog()

  useEffect(() => {
    if (blocker.state !== 'blocked') {
      return
    }

    let cancelled = false

    async function check() {
      const accepted = await confirm({
        title,
        description,
        confirmText,
        cancelText,
        confirmColor: 'amber',
      })

      if (cancelled) {
        return
      }

      if (accepted) {
        const proceed = blocker.proceed
        if (proceed) {
          proceed()
        }
      } else {
        const reset = blocker.reset
        if (reset) {
          reset()
        }
      }
    }

    void check()

    return () => {
      cancelled = true
    }
  }, [blocker, cancelText, confirm, confirmText, description, title])
}
