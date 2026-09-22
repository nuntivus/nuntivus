import { Button, Card, Flex, Heading, Text, TextArea } from '@radix-ui/themes'
import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAppDispatch, useAppSelector } from '../store/hooks'
import { signIn } from '../store/auth-slice'

export function LoginPage() {
  const [tokenInput, setTokenInput] = useState('')
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()
  const token = useAppSelector((state) => state.auth.token)

  const redirectTo = (location.state as { from?: { pathname?: string } } | null)
    ?.from?.pathname

  if (token) {
    return <Navigate to={redirectTo || '/'} replace />
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!tokenInput.trim()) {
      return
    }

    dispatch(signIn(tokenInput))
    navigate(redirectTo || '/', { replace: true })
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
            <TextArea
              value={tokenInput}
              onChange={(event) => setTokenInput(event.target.value)}
              placeholder="eyJhbGciOi..."
              rows={6}
            />
            <Button type="submit" disabled={!tokenInput.trim()}>
              Sign in
            </Button>
          </Flex>
        </form>
      </Card>
    </Flex>
  )
}
