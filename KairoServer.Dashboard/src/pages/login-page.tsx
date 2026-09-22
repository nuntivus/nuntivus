import { AlertDialog, Button, Card, Flex, Heading, Spinner, Text, TextArea } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAppDispatch, useAppSelector } from '../store/hooks'
import { clearAuthError, loginWithToken } from '../store/auth-slice'

export function LoginPage() {
  const [tokenInput, setTokenInput] = useState('')
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()
  const { token, status, error } = useAppSelector((state) => state.auth)

  const redirectTo = (location.state as { from?: { pathname?: string } } | null)
    ?.from?.pathname

  if (status === 'authenticated' && token) {
    return <Navigate to={redirectTo || '/'} replace />
  }

  const restoringSession = status === 'loading' && !!token

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!tokenInput.trim()) {
      return
    }

    const result = await dispatch(loginWithToken(tokenInput))

    if (loginWithToken.fulfilled.match(result)) {
      navigate(redirectTo || '/', { replace: true })
    }
  }

  return (
    <Flex align="center" justify="center" minHeight="100dvh" px="4">
      <Card size="3" style={{ width: '100%', maxWidth: 560 }}>
        <form onSubmit={handleSubmit}>
          <Flex direction="column" gap="4">
            <Heading size="6">Admin login</Heading>
            <Text size="2" color="gray">
              Paste a JWT bearer token to start an authenticated admin session.
            </Text>

            {restoringSession ? (
              <Flex align="center" gap="2">
                <Spinner size="2" />
                <Text size="2" color="gray">
                  Validating existing session...
                </Text>
              </Flex>
            ) : null}

            {error ? (
              <AlertDialog.Root open>
                <AlertDialog.Content maxWidth="520px">
                  <AlertDialog.Title>Login failed</AlertDialog.Title>
                  <AlertDialog.Description>{error}</AlertDialog.Description>
                  <Flex justify="end" mt="4">
                    <AlertDialog.Action>
                      <Button variant="soft" onClick={() => dispatch(clearAuthError())}>
                        Dismiss
                      </Button>
                    </AlertDialog.Action>
                  </Flex>
                </AlertDialog.Content>
              </AlertDialog.Root>
            ) : null}

            <TextArea
              value={tokenInput}
              onChange={(event) => setTokenInput(event.target.value)}
              placeholder="eyJhbGciOi..."
              rows={6}
              disabled={restoringSession}
            />
            <Button
              type="submit"
              disabled={!tokenInput.trim() || status === 'loading' || restoringSession}
            >
              {status === 'loading' ? 'Signing in...' : 'Sign in'}
            </Button>
          </Flex>
        </form>
      </Card>
    </Flex>
  )
}
