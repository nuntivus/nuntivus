import {
  Avatar,
  Box,
  Button,
  DropdownMenu,
  Flex,
  Heading,
  Link,
  ScrollArea,
  Select,
  Text,
} from '@radix-ui/themes'
import {
  NavLink,
  Outlet,
  useLocation,
  useNavigate,
  type To,
} from 'react-router-dom'
import { useAppContext } from '../context/use-app-context'
import { hasPermission } from '../auth/permissions'
import { useAppDispatch, useAppSelector } from '../store/hooks'
import { signOut } from '../store/auth-slice'
import { setSidebarOpen, toggleSidebar } from '../store/ui-slice'

const navItems = [
  { to: '/' as To, label: 'Overview' },
  { to: '/customers' as To, label: 'Customers', permission: 'customers.read' },
  { to: '/contracts' as To, label: 'Contracts', permission: 'contracts.read' },
  { to: '/events' as To, label: 'Events', permission: 'events.read' },
  { to: '/event-definitions' as To, label: 'Event Definitions', permission: 'event-definitions.read' },
  { to: '/deliveries' as To, label: 'Deliveries' },
  { to: '/deliveries/failures' as To, label: 'Failed Deliveries', permission: 'delivery-failures.read' },
  { to: '/users' as To, label: 'Users', permission: 'users.read' },
  { to: '/settings' as To, label: 'Settings', permission: 'settings.write' },
]

const breadcrumbLabels: Record<string, string> = {
  customers: 'Customers',
  contracts: 'Contracts',
  events: 'Events',
  'event-definitions': 'Event Definitions',
  deliveries: 'Deliveries',
  failures: 'Failed Deliveries',
  users: 'Users',
  settings: 'Settings',
  forbidden: 'Forbidden',
}

export function AdminLayout() {
  const dispatch = useAppDispatch()
  const navigate = useNavigate()
  const location = useLocation()
  const sidebarOpen = useAppSelector((state) => state.ui.sidebarOpen)
  const { token, currentUser } = useAppSelector((state) => state.auth)
  const { currentWorkspace, setCurrentWorkspace } = useAppContext()
  const canAccessSettings = hasPermission(currentUser, 'settings.write')
  const pathSegments = location.pathname.split('/').filter(Boolean)

  const breadcrumbs = pathSegments.map((segment, index) => {
    const path = `/${pathSegments.slice(0, index + 1).join('/')}`
    const label =
      breadcrumbLabels[segment] ??
      segment.charAt(0).toUpperCase() + segment.slice(1)
    const isLast = index === pathSegments.length - 1

    return { path, label, isLast }
  })

  function handleSignOut() {
    dispatch(signOut())
    navigate('/login', { replace: true })
  }

  function handleNavigationClick() {
    if (window.matchMedia('(max-width: 960px)').matches) {
      dispatch(setSidebarOpen(false))
    }
  }

  return (
    <Flex className="dashboard-layout" minHeight="100dvh">
      <aside className={`dashboard-sidebar ${sidebarOpen ? 'open' : 'closed'}`}>
        <Flex direction="column" height="100%" p="4" gap="4">
          <Heading size="4">Kairo Admin</Heading>

          <ScrollArea type="auto" scrollbars="vertical" style={{ flex: 1 }}>
            <nav className="sidebar-nav" aria-label="Sidebar Navigation">
              {navItems.map((item) => (
                item.permission && !hasPermission(currentUser, item.permission) ? (
                  <span key={item.to.toString()} className="sidebar-link disabled">
                    {item.label}
                  </span>
                ) : (
                  <NavLink
                    key={item.to.toString()}
                    to={item.to}
                    className={({ isActive }) =>
                      isActive ? 'sidebar-link active' : 'sidebar-link'
                    }
                    onClick={handleNavigationClick}
                  >
                    {item.label}
                  </NavLink>
                )
              ))}
            </nav>
          </ScrollArea>

          <Text size="1" color="gray">
            Workspace: {currentWorkspace}
          </Text>
        </Flex>
      </aside>

      <Flex direction="column" className="dashboard-main" style={{ flex: 1 }}>
        <header className="dashboard-header">
          <Flex align="center" justify="between" gap="4">
            <Flex align="center" gap="3">
              <Button
                type="button"
                variant="soft"
                onClick={() => dispatch(toggleSidebar())}
              >
                {sidebarOpen ? 'Hide menu' : 'Show menu'}
              </Button>

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
            </Flex>

            <DropdownMenu.Root>
              <DropdownMenu.Trigger>
                <Button variant="ghost" type="button">
                  <Avatar fallback="AD" size="2" radius="full" />
                  {currentUser?.displayName?.trim() || currentUser?.subject || 'User'}
                </Button>
              </DropdownMenu.Trigger>
              <DropdownMenu.Content>
                <DropdownMenu.Label>Session</DropdownMenu.Label>
                <DropdownMenu.Item>{token ? 'Authenticated' : 'Anonymous'}</DropdownMenu.Item>
                {currentUser?.subject ? (
                  <DropdownMenu.Item disabled>{currentUser.subject}</DropdownMenu.Item>
                ) : null}
                <DropdownMenu.Separator />
                <DropdownMenu.Item
                  onSelect={() => navigate('/settings')}
                  disabled={!canAccessSettings}
                >
                  Profile settings
                </DropdownMenu.Item>
                <DropdownMenu.Item onSelect={() => navigate('/forbidden')}>
                  Permission preview
                </DropdownMenu.Item>
                <DropdownMenu.Separator />
                <DropdownMenu.Item color="red" onSelect={handleSignOut}>
                  Sign out
                </DropdownMenu.Item>
              </DropdownMenu.Content>
            </DropdownMenu.Root>
          </Flex>

          <Flex align="center" gap="2" mt="2">
            <Link asChild color="gray" underline="hover">
              <NavLink to="/">Home</NavLink>
            </Link>
            {breadcrumbs.map((item) => (
              <Flex key={item.path} align="center" gap="2">
                <Text size="1" color="gray">
                  /
                </Text>
                {item.isLast ? (
                  <Text size="1" weight="medium">
                    {item.label}
                  </Text>
                ) : (
                  <Link asChild color="gray" underline="hover">
                    <NavLink to={item.path}>{item.label}</NavLink>
                  </Link>
                )}
              </Flex>
            ))}
            {breadcrumbs.length === 0 ? (
              <Text size="1" weight="medium">
                Overview
              </Text>
            ) : null}
          </Flex>
        </header>

        <main className="dashboard-content">
          <Outlet />
        </main>

        <Box className="dashboard-footer" px="4" py="3">
          <Text size="2" color="gray">
            Dashboard foundation · Nav: {sidebarOpen ? 'visible' : 'hidden'} · Session:{' '}
            {token ? 'authenticated' : 'anonymous'}
          </Text>
        </Box>
      </Flex>

      {sidebarOpen ? (
        <button
          type="button"
          className="sidebar-backdrop"
          aria-label="Close sidebar"
          onClick={() => dispatch(setSidebarOpen(false))}
        />
      ) : null}
    </Flex>
  )
}
