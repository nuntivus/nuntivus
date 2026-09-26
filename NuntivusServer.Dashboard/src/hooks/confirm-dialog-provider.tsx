import {
  AlertDialog,
  Button,
  Flex,
  Text,
} from '@radix-ui/themes'
import {
  useCallback,
  useMemo,
  useState,
  type PropsWithChildren,
} from 'react'
import {
  ConfirmDialogContext,
  type ConfirmOptions,
  type ConfirmHandler,
} from './use-confirm-dialog'

type PendingConfirm = {
  options: ConfirmOptions
  resolve: (accepted: boolean) => void
}

export function ConfirmDialogProvider({ children }: PropsWithChildren) {
  const [pending, setPending] = useState<PendingConfirm | null>(null)

  const confirm = useCallback<ConfirmHandler>((options) => {
    return new Promise((resolve) => {
      setPending({ options, resolve })
    })
  }, [])

  const closeWith = useCallback((accepted: boolean) => {
    setPending((current) => {
      if (!current) {
        return null
      }

      current.resolve(accepted)
      return null
    })
  }, [])

  const value = useMemo(() => confirm, [confirm])

  return (
    <ConfirmDialogContext.Provider value={value}>
      {children}
      <AlertDialog.Root open={!!pending}>
        <AlertDialog.Content maxWidth="540px" aria-describedby="confirm-dialog-description">
          <AlertDialog.Title>{pending?.options.title}</AlertDialog.Title>
          <AlertDialog.Description id="confirm-dialog-description">
            <Text>{pending?.options.description}</Text>
          </AlertDialog.Description>
          <Flex gap="2" justify="end" mt="4">
            <AlertDialog.Cancel>
              <Button variant="soft" onClick={() => closeWith(false)}>
                {pending?.options.cancelText ?? 'Cancel'}
              </Button>
            </AlertDialog.Cancel>
            <AlertDialog.Action>
              <Button
                color={pending?.options.confirmColor ?? 'indigo'}
                onClick={() => closeWith(true)}
              >
                {pending?.options.confirmText ?? 'Confirm'}
              </Button>
            </AlertDialog.Action>
          </Flex>
        </AlertDialog.Content>
      </AlertDialog.Root>
    </ConfirmDialogContext.Provider>
  )
}
