import {
  Avatar,
  Box,
  DropdownMenu,
  Flex,
  IconButton,
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
import type { ComponentType, SVGProps } from 'react'
import { useAppContext } from '../context/use-app-context'
import { hasPermission } from '../auth/permissions'
import { clearSensitiveClientState } from '../app/auth-security'
import { queryClient } from '../app/queryClient'
import { useAppDispatch, useAppSelector } from '../store/hooks'
import { signOut } from '../store/auth-slice'
import { setSidebarOpen, toggleSidebar } from '../store/ui-slice'
import {
  AuditLogsIcon,
  ChevronRightIcon,
  ContractsIcon,
  CustomersIcon,
  DeliveriesIcon,
  EventDefinitionsIcon,
  EventsIcon,
  FailedDeliveriesIcon,
  MenuIcon,
  OverviewIcon,
  SettingsIcon,
  UsersIcon,
} from '../components/icons/nav-icons'

type NavIcon = ComponentType<SVGProps<SVGSVGElement>>

interface NavItem {
  to: To
  label: string
  icon: NavIcon
  permission?: string
}

interface NavGroup {
  heading?: string
  items: NavItem[]
}

const navGroups: NavGroup[] = [
  {
    items: [{ to: '/' as To, label: 'Overview', icon: OverviewIcon }],
  },
  {
    heading: 'Manage',
    items: [
      { to: '/customers' as To, label: 'Customers', icon: CustomersIcon, permission: 'customers.read' },
      { to: '/contracts' as To, label: 'Contracts', icon: ContractsIcon, permission: 'contracts.read' },
      { to: '/secrets' as To, label: 'Secrets', icon: SettingsIcon, permission: 'contracts.write' },
      { to: '/test-events' as To, label: 'Test events', icon: EventsIcon, permission: 'contracts.read' },
      { to: '/events' as To, label: 'Events', icon: EventsIcon, permission: 'events.read' },
      { to: '/event-definitions' as To, label: 'Event Definitions', icon: EventDefinitionsIcon, permission: 'event-definitions.read' },
    ],
  },
  {
    heading: 'Operations',
    items: [
      { to: '/deliveries' as To, label: 'Deliveries', icon: DeliveriesIcon },
      { to: '/deliveries/failures' as To, label: 'Failed Deliveries', icon: FailedDeliveriesIcon, permission: 'delivery-failures.read' },
      { to: '/audit-logs' as To, label: 'Audit Logs', icon: AuditLogsIcon, permission: 'audit-logs.read' },
    ],
  },
  {
    heading: 'Administration',
    items: [
      { to: '/users' as To, label: 'Users', icon: UsersIcon, permission: 'users.read' },
      { to: '/settings' as To, label: 'Settings', icon: SettingsIcon, permission: 'settings.write' },
    ],
  },
]

const breadcrumbLabels: Record<string, string> = {
  customers: 'Customers',
  contracts: 'Contracts',
  secrets: 'Secrets',
  'test-events': 'Test events',
  events: 'Events',
  'event-definitions': 'Event Definitions',
  deliveries: 'Deliveries',
  failures: 'Failed Deliveries',
  'audit-logs': 'Audit Logs',
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
    clearSensitiveClientState(queryClient)
    navigate('/login', { replace: true })
  }

  function handleNavigationClick() {
    if (window.matchMedia('(max-width: 960px)').matches) {
      dispatch(setSidebarOpen(false))
    }
  }

  return (
    <Flex className="dashboard-layout" minHeight="100dvh">
      <a href="#main-content" className="skip-link">Skip to main content</a>
      <aside className={`dashboard-sidebar ${sidebarOpen ? 'open' : 'closed'}`}>
        <div className="sidebar-inner">
          <div className="sidebar-brand">
            <span className="sidebar-brand-mark" aria-hidden="true">
              K
            </span>
            <span className="sidebar-brand-text">Nuntivus Admin</span>
          </div>

          <nav className="sidebar-nav" aria-label="Primary navigation">
            {navGroups.map((group, groupIndex) => (
              <div className="sidebar-group" key={group.heading ?? `group-${groupIndex}`}>
                {group.heading ? (
                  <div className="sidebar-group-heading">{group.heading}</div>
                ) : null}
                {group.items.map((item) => {
                  const Icon = item.icon
                  const disabled =
                    item.permission && !hasPermission(currentUser, item.permission)

                  return disabled ? (
                    <span key={item.to.toString()} className="sidebar-link disabled">
                      <Icon className="sidebar-link-icon" />
                      <span>{item.label}</span>
                    </span>
                  ) : (
                    <NavLink
                      key={item.to.toString()}
                      to={item.to}
                      end={item.to === '/'}
                      className={({ isActive }) =>
                        isActive ? 'sidebar-link active' : 'sidebar-link'
                      }
                      onClick={handleNavigationClick}
                    >
                      <Icon className="sidebar-link-icon" />
                      <span>{item.label}</span>
                    </NavLink>
                  )
                })}
              </div>
            ))}
          </nav>

          <Text size="1" className="sidebar-footer-text">
            Workspace: {currentWorkspace}
          </Text>
        </div>
      </aside>

      <Flex direction="column" className="dashboard-main" style={{ flex: 1 }}>
        <header className="dashboard-header">
          <div className="header-left">
            <IconButton
              type="button"
              variant="ghost"
              color="gray"
              className="header-menu-toggle"
              aria-label={sidebarOpen ? 'Hide menu' : 'Show menu'}
              aria-pressed={sidebarOpen}
              onClick={() => dispatch(toggleSidebar())}
            >
              <MenuIcon />
            </IconButton>

            <Select.Root
              value={currentWorkspace}
              onValueChange={setCurrentWorkspace}
              size="1"
            >
              <Select.Trigger
                variant="soft"
                color="gray"
                aria-label="Environment"
              />
              <Select.Content>
                <Select.Item value="default">Default</Select.Item>
                <Select.Item value="operations">Operations</Select.Item>
                <Select.Item value="audit">Audit</Select.Item>
              </Select.Content>
            </Select.Root>

            <span className="header-divider" aria-hidden="true" />

            <nav className="header-breadcrumbs" aria-label="Breadcrumb">
              <NavLink to="/" className="crumb crumb-link">
                Home
              </NavLink>
              {breadcrumbs.length === 0 ? (
                <>
                  <ChevronRightIcon className="crumb-sep" />
                  <span className="crumb crumb-current">Overview</span>
                </>
              ) : null}
              {breadcrumbs.map((item) => (
                <span key={item.path} className="crumb-segment">
                  <ChevronRightIcon className="crumb-sep" />
                  {item.isLast ? (
                    <span className="crumb crumb-current">{item.label}</span>
                  ) : (
                    <NavLink to={item.path} className="crumb crumb-link">
                      {item.label}
                    </NavLink>
                  )}
                </span>
              ))}
            </nav>
          </div>

          <div className="header-right">
            <DropdownMenu.Root>
              <DropdownMenu.Trigger>
                <button type="button" className="header-user-trigger">
                  <Avatar fallback="AD" size="1" radius="full" />
                  <span className="header-user-name">
                    {currentUser?.displayName?.trim() ||
                      currentUser?.subject ||
                      'User'}
                  </span>
                </button>
              </DropdownMenu.Trigger>
              <DropdownMenu.Content>
                <DropdownMenu.Label>Session</DropdownMenu.Label>
                <DropdownMenu.Item>
                  {token ? 'Authenticated' : 'Anonymous'}
                </DropdownMenu.Item>
                {currentUser?.subject ? (
                  <DropdownMenu.Item disabled>
                    {currentUser.subject}
                  </DropdownMenu.Item>
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
          </div>
        </header>

        <main id="main-content" className="dashboard-content" tabIndex={-1}>
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
