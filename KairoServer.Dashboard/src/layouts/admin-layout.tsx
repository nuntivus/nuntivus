import {
  Box,
  Button,
  Container,
  Flex,
  Heading,
  Link,
  Select,
  Separator,
  Text,
} from '@radix-ui/themes'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAppContext } from '../context/use-app-context'
import { useAppDispatch, useAppSelector } from '../store/hooks'
import { signOut } from '../store/auth-slice'
import { toggleSidebar } from '../store/ui-slice'

const navItems = [
  { to: '/', label: 'Overview' },
  { to: '/deliveries', label: 'Deliveries' },
  { to: '/settings', label: 'Settings' },
]

export function AdminLayout() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const sidebarOpen = useAppSelector((state) => state.ui.sidebarOpen)
  const token = useAppSelector((state) => state.auth.token)
  const { currentWorkspace, setCurrentWorkspace } = useAppContext()

  function handleSignOut() {
    dispatch(signOut())
    navigate('/login', { replace: true })
  }

  return (
    <Flex direction="column" minHeight="100dvh">
      <Box px="6" py="4" style={{ borderBottom: '1px solid var(--gray-a5)' }}>
        <Container size="4">
          <Flex align="center" justify="between" gap="4">
            <Heading size="5">Kairo Admin</Heading>
            <Flex align="center" gap="4">
              <Select.Root
                value={currentWorkspace}
                onValueChange={setCurrentWorkspace}
              >
                <Select.Trigger aria-label="Workspace" />
                <Select.Content>
                  <Select.Item value="default">Default</Select.Item>
                  <Select.Item value="operations">Operations</Select.Item>
                  <Select.Item value="audit">Audit</Select.Item>
                </Select.Content>
              </Select.Root>

              <Button
                type="button"
                variant="soft"
                onClick={() => dispatch(toggleSidebar())}
              >
                {sidebarOpen ? 'Hide nav' : 'Show nav'}
              </Button>

              <Button type="button" variant="outline" onClick={handleSignOut}>
                Sign out
              </Button>

              {sidebarOpen
                ? navItems.map((item) => (
                    <Link key={item.to} asChild weight="medium">
                      <NavLink to={item.to}>{item.label}</NavLink>
                    </Link>
                  ))
                : null}
            </Flex>
          </Flex>
        </Container>
      </Box>

      <Container size="4" px="6" py="5" style={{ flex: 1 }}>
        <Outlet />
      </Container>

      <Separator size="4" />
      <Box px="6" py="3">
        <Container size="4">
          <Text size="2" color="gray">
            Dashboard MVP foundation · Workspace: {currentWorkspace} · Nav:{' '}
            {sidebarOpen ? 'visible' : 'hidden'} · Session:{' '}
            {token ? 'authenticated' : 'anonymous'}
          </Text>
        </Container>
      </Box>
    </Flex>
  )
}
