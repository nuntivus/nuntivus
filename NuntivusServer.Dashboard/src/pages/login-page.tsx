import { AlertDialog, Button, Card, Flex, Heading, Spinner, Text, TextField } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import logo from '../assets/nuntivus-logo.svg'
import { FieldErrorText } from '../components/forms/form-feedback'
import { useFormValidation } from '../hooks/use-form-validation'
import { useAppDispatch, useAppSelector } from '../store/hooks'
import { clearAuthError, loginWithCredentials } from '../store/auth-slice'
import './login-page.css'

function credentialErrors(username: string, password: string) {
  return {
    usernameError: !username.trim() ? 'Username is required.' : null,
    passwordError: !password.trim() ? 'Password is required.' : null,
  }
}

export function LoginPage() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()
  const { token, status, error } = useAppSelector((state) => state.auth)

  const redirectTo = (location.state as { from?: { pathname?: string } } | null)
    ?.from?.pathname
  const validation = useFormValidation(credentialErrors(username, password))
  const { usernameError, passwordError } = validation.fieldErrors

  if (status === 'authenticated' && token) {
    return <Navigate to={redirectTo || '/'} replace />
  }

  const restoringSession = status === 'loading' && !!token

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (status === 'loading') {
      return
    }

    const formData = new FormData(event.currentTarget)
    const submittedUsername = String(formData.get('username') ?? '')
    const submittedPassword = String(formData.get('password') ?? '')
    setUsername(submittedUsername)
    setPassword(submittedPassword)

    if (!validation.validate(credentialErrors(submittedUsername, submittedPassword))) {
      return
    }

    const result = await dispatch(loginWithCredentials({ username: submittedUsername, password: submittedPassword }))

    if (loginWithCredentials.fulfilled.match(result)) {
      navigate(redirectTo || '/', { replace: true })
    }
  }

  return (
    <Flex asChild align="center" justify="center" className="login-page">
      <main aria-labelledby="login-heading">
        <Flex direction="column" gap="5" className="login-shell">
          <Flex direction="column" align="center" gap="2" className="login-brand">
            <img src={logo} alt="Nuntivus logo" width="72" height="60" className="login-logo" />
            <Text className="login-brand-name">Nuntivus</Text>
            <Text size="2" color="gray">Event delivery, under control.</Text>
          </Flex>
          <Card size="3" className="login-card">
            <form onSubmit={handleSubmit} aria-labelledby="login-heading">
              <Flex direction="column" gap="4">
                <Flex direction="column" gap="2" mb="2" className="login-heading">
                  <Heading id="login-heading" size="6">Admin login</Heading>
                  <Text size="2" color="gray">
                    Sign in to your Nuntivus dashboard.
                  </Text>
                </Flex>

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

                <Flex direction="column" gap="2">
                  <Text as="label" htmlFor="login-username" size="2" weight="medium">Username</Text>
                  <TextField.Root
                    id="login-username"
                    name="username"
                    autoComplete="username"
                    autoCapitalize="none"
                    spellCheck={false}
                    size="3"
                    className="login-field"
                    value={username}
                    onChange={(event) => setUsername(event.target.value)}
                    placeholder="Enter your username"
                    disabled={restoringSession}
                    aria-invalid={usernameError ? 'true' : 'false'}
                    aria-describedby={usernameError ? 'login-username-error' : undefined}
                  />
                  <FieldErrorText id="login-username-error" message={usernameError} />
                </Flex>

                <Flex direction="column" gap="2">
                  <Text as="label" htmlFor="login-password" size="2" weight="medium">Password</Text>
                  <TextField.Root
                    id="login-password"
                    name="password"
                    autoComplete="current-password"
                    size="3"
                    className="login-field"
                    type="password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    placeholder="Enter your password"
                    disabled={restoringSession}
                    aria-invalid={passwordError ? 'true' : 'false'}
                    aria-describedby={passwordError ? 'login-password-error' : undefined}
                  />
                  <FieldErrorText id="login-password-error" message={passwordError} />
                </Flex>

                <Button
                  type="submit"
                  size="3"
                  className="login-submit"
                  loading={status === 'loading'}
                  disabled={status === 'loading' || restoringSession}
                >
                  {status === 'loading' ? 'Signing in...' : 'Sign in'}
                </Button>
              </Flex>
            </form>
          </Card>
          <Text size="1" className="login-footer">
            Administrator access only
          </Text>
        </Flex>
      </main>
    </Flex>
  )
}
