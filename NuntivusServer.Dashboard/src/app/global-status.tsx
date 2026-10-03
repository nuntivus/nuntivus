import { useIsFetching, useIsMutating } from '@tanstack/react-query'
import { Button, Card, Flex, Spinner, Text } from '@radix-ui/themes'
import { useEffect, useRef } from 'react'
import { useLocation } from 'react-router-dom'
import { clearGlobalError, setGlobalError } from '../store/app-shell-slice'
import { useAppDispatch, useAppSelector } from '../store/hooks'

interface ApiErrorDetail {
  message?: string
  route?: string
  request?: string
}

export function GlobalStatus() {
  const dispatch = useAppDispatch()
  const location = useLocation()
  const errorKey = useRef<string | null>(null)
  const globalError = useAppSelector((state) => state.appShell.globalError)
  const isFetching = useIsFetching()
  const isMutating = useIsMutating()
  const isLoading = isFetching > 0 || isMutating > 0

  useEffect(() => {
    function handleApiError(event: Event) {
      const customEvent = event as CustomEvent<ApiErrorDetail>
      const { route, request } = customEvent.detail ?? {}
      if (route !== window.location.pathname + window.location.search) return
      const message = customEvent.detail?.message?.trim() || 'Unexpected API error'
      const key = `${route}:${request}:${message}`
      if (errorKey.current === key) return
      errorKey.current = key
      dispatch(setGlobalError(message))
    }

    function handleRecovery(event: Event) {
      const { route, request } = (event as CustomEvent<ApiErrorDetail>).detail ?? {}
      if (errorKey.current?.startsWith(`${route}:${request}:`)) {
        errorKey.current = null
        dispatch(clearGlobalError())
      }
    }

    window.addEventListener('nuntivus:api-error', handleApiError)
    window.addEventListener('nuntivus:api-recovered', handleRecovery)

    return () => {
      window.removeEventListener('nuntivus:api-error', handleApiError)
      window.removeEventListener('nuntivus:api-recovered', handleRecovery)
    }
  }, [dispatch])

  useEffect(() => {
    errorKey.current = null
    dispatch(clearGlobalError())
  }, [dispatch, location.key])

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
              onClick={() => { errorKey.current = null; dispatch(clearGlobalError()) }}
            >
              Dismiss
            </Button>
          </Flex>
        </Card>
      ) : null}
    </>
  )
}
