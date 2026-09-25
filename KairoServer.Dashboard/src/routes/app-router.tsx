import { lazy, Suspense } from 'react'
import { Route, Routes } from 'react-router-dom'
import { AdminLayout } from '../layouts/admin-layout'
import { RequireAuth } from './require-auth'
import { RequirePermission } from './require-permission'

const AuditLogsPage = lazy(() => import('../pages/audit-logs-page').then((module) => ({ default: module.AuditLogsPage })))
const DashboardPage = lazy(() => import('../pages/dashboard-page').then((module) => ({ default: module.DashboardPage })))
const CustomersPage = lazy(() => import('../pages/customers-page').then((module) => ({ default: module.CustomersPage })))
const CustomerDetailPage = lazy(() => import('../pages/customer-detail-page').then((module) => ({ default: module.CustomerDetailPage })))
const ContractsPage = lazy(() => import('../pages/contracts-page').then((module) => ({ default: module.ContractsPage })))
const ContractCreatePage = lazy(() => import('../pages/contract-create-page').then((module) => ({ default: module.ContractCreatePage })))
const ContractDetailPage = lazy(() => import('../pages/contract-detail-page').then((module) => ({ default: module.ContractDetailPage })))
const ContractEditPage = lazy(() => import('../pages/contract-edit-page').then((module) => ({ default: module.ContractEditPage })))
const DeliveriesPage = lazy(() => import('../pages/deliveries-page').then((module) => ({ default: module.DeliveriesPage })))
const DeliveryDetailPage = lazy(() => import('../pages/delivery-detail-page').then((module) => ({ default: module.DeliveryDetailPage })))
const FailedDeliveriesPage = lazy(() => import('../pages/failed-deliveries-page').then((module) => ({ default: module.FailedDeliveriesPage })))
const EventDetailPage = lazy(() => import('../pages/event-detail-page').then((module) => ({ default: module.EventDetailPage })))
const EventDefinitionDetailPage = lazy(() => import('../pages/event-definition-detail-page').then((module) => ({ default: module.EventDefinitionDetailPage })))
const EventDefinitionsPage = lazy(() => import('../pages/event-definitions-page').then((module) => ({ default: module.EventDefinitionsPage })))
const EventsPage = lazy(() => import('../pages/events-page').then((module) => ({ default: module.EventsPage })))
const TestEventsPage = lazy(() => import('../pages/test-events-page').then((module) => ({ default: module.TestEventsPage })))
const ForbiddenPage = lazy(() => import('../pages/forbidden-page').then((module) => ({ default: module.ForbiddenPage })))
const LoginPage = lazy(() => import('../pages/login-page').then((module) => ({ default: module.LoginPage })))
const NotFoundPage = lazy(() => import('../pages/not-found-page').then((module) => ({ default: module.NotFoundPage })))
const SettingsPage = lazy(() => import('../pages/settings-page').then((module) => ({ default: module.SettingsPage })))
const UserDetailPage = lazy(() => import('../pages/user-detail-page').then((module) => ({ default: module.UserDetailPage })))
const UsersPage = lazy(() => import('../pages/users-page').then((module) => ({ default: module.UsersPage })))

export function AppRouter() {
  return (
    <div className="app-shell">
      <Suspense fallback={null}>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route element={<RequireAuth />}>
            <Route element={<AdminLayout />}>
              <Route index element={<DashboardPage />} />
              <Route path="deliveries" element={<DeliveriesPage />} />
              <Route path="deliveries/:id" element={<DeliveryDetailPage />} />
              <Route element={<RequirePermission permissions={['delivery-failures.read']} />}>
                <Route path="deliveries/failures" element={<FailedDeliveriesPage />} />
              </Route>
              <Route element={<RequirePermission permissions={['audit-logs.read']} />}>
                <Route path="audit-logs" element={<AuditLogsPage />} />
              </Route>
              <Route element={<RequirePermission permissions={['events.read']} />}>
                <Route path="events" element={<EventsPage />} />
                <Route path="events/:id" element={<EventDetailPage />} />
              </Route>
              <Route element={<RequirePermission permissions={['event-definitions.read']} />}>
                <Route path="event-definitions" element={<EventDefinitionsPage />} />
                <Route path="event-definitions/:eventType" element={<EventDefinitionDetailPage />} />
              </Route>
              <Route element={<RequirePermission permissions={['customers.read']} />}>
                <Route path="customers" element={<CustomersPage />} />
                <Route path="customers/:id" element={<CustomerDetailPage />} />
              </Route>
              <Route element={<RequirePermission permissions={['contracts.read']} />}>
                <Route path="contracts" element={<ContractsPage />} />
                <Route path="contracts/:id" element={<ContractDetailPage />} />
                <Route path="test-events" element={<TestEventsPage />} />
              </Route>
              <Route element={<RequirePermission permissions={['contracts.write']} />}>
                <Route path="contracts/new" element={<ContractCreatePage />} />
                <Route path="contracts/:id/edit" element={<ContractEditPage />} />
              </Route>
              <Route element={<RequirePermission permissions={['users.read']} />}>
                <Route path="users" element={<UsersPage />} />
                <Route path="users/:subject" element={<UserDetailPage />} />
              </Route>
              <Route path="settings" element={<SettingsPage />} />
              <Route path="forbidden" element={<ForbiddenPage />} />
              <Route path="*" element={<NotFoundPage />} />
            </Route>
          </Route>
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </Suspense>
    </div>
  )
}
