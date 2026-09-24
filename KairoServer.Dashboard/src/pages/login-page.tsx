import { AlertDialog, Button, Card, Flex, Heading, Spinner, Text, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { FieldErrorText } from '../components/forms/form-feedback'
import { useAppDispatch, useAppSelector } from '../store/hooks'
import { clearAuthError, loginWithCredentials } from '../store/auth-slice'

export function LoginPage() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()
  const { token, status, error } = useAppSelector((state) => state.auth)

  const redirectTo = (location.state as { from?: { pathname?: string } } | null)
    ?.from?.pathname
  const usernameError = !username.trim() ? 'Username is required.' : null
  const passwordError = !password.trim() ? 'Password is required.' : null

  if (status === 'authenticated' && token) {
    return <Navigate to={redirectTo || '/'} replace />
  }

  const restoringSession = status === 'loading' && !!token

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (usernameError || passwordError) {
      return
    }

    const result = await dispatch(loginWithCredentials({ username, password }))

    if (loginWithCredentials.fulfilled.match(result)) {
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
              Sign in with your admin username and password.
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

            <TextField.Root
              value={username}
              onChange={(event) => setUsername(event.target.value)}
              placeholder="Username"
              disabled={restoringSession}
              aria-invalid={usernameError ? 'true' : 'false'}
              aria-describedby={usernameError ? 'login-username-error' : undefined}
            />
            <FieldErrorText id="login-username-error" message={usernameError} />

            <TextField.Root
              type="password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              placeholder="Password"
              disabled={restoringSession}
              aria-invalid={passwordError ? 'true' : 'false'}
              aria-describedby={passwordError ? 'login-password-error' : undefined}
            />
            <FieldErrorText id="login-password-error" message={passwordError} />

            <Button
              type="submit"
              disabled={!!usernameError || !!passwordError || status === 'loading' || restoringSession}
            >
              {status === 'loading' ? 'Signing in...' : 'Sign in'}
            </Button>
          </Flex>
        </form>
      </Card>
    </Flex>
  )
}
