import { useIsFetching, useIsMutating } from '@tanstack/react-query'
import { Button, Card, Flex, Spinner, Text } from '@radix-ui/themes'
import { useEffect } from 'react'
import { clearGlobalError, setGlobalError } from '../store/app-shell-slice'
import { useAppDispatch, useAppSelector } from '../store/hooks'

interface ApiErrorDetail {
  message?: string
}

export function GlobalStatus() {
  const dispatch = useAppDispatch()
  const globalError = useAppSelector((state) => state.appShell.globalError)
  const isFetching = useIsFetching()
  const isMutating = useIsMutating()
  const isLoading = isFetching > 0 || isMutating > 0

  useEffect(() => {
    function handleApiError(event: Event) {
      const customEvent = event as CustomEvent<ApiErrorDetail>
      const message = customEvent.detail?.message?.trim() || 'Unexpected API error'
      dispatch(setGlobalError(message))
    }

    window.addEventListener('nuntivus:api-error', handleApiError)

    return () => {
      window.removeEventListener('nuntivus:api-error', handleApiError)
    }
  }, [dispatch])

  return (
    <>
      {isLoading ? (
        <div className="global-loading" role="status" aria-live="polite">
          <Spinner size="1" />
          <Text size="1">Loading...</Text>
        </div>
      ) : null}

      {globalError ? (
        <Card className="global-error" size="2">
          <Flex align="center" gap="3" justify="between">
            <Text size="2">{globalError}</Text>
            <Button
              variant="soft"
              color="red"
              onClick={() => dispatch(clearGlobalError())}
            >
              Dismiss
            </Button>
          </Flex>
        </Card>
      ) : null}
    </>
  )
}
