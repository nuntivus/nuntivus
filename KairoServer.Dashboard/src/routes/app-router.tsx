import { Route, Routes } from 'react-router-dom'
import { AdminLayout } from '../layouts/admin-layout'
import { DashboardPage } from '../pages/dashboard-page'
import { CustomersPage } from '../pages/customers-page'
import { CustomerDetailPage } from '../pages/customer-detail-page'
import { ContractsPage } from '../pages/contracts-page'
import { ContractCreatePage } from '../pages/contract-create-page'
import { ContractDetailPage } from '../pages/contract-detail-page'
import { ContractEditPage } from '../pages/contract-edit-page'
import { DeliveriesPage } from '../pages/deliveries-page'
import { DeliveryDetailPage } from '../pages/delivery-detail-page'
import { FailedDeliveriesPage } from '../pages/failed-deliveries-page'
import { EventDetailPage } from '../pages/event-detail-page'
import { EventDefinitionDetailPage } from '../pages/event-definition-detail-page'
import { EventDefinitionsPage } from '../pages/event-definitions-page'
import { EventsPage } from '../pages/events-page'
import { ForbiddenPage } from '../pages/forbidden-page'
import { LoginPage } from '../pages/login-page'
import { NotFoundPage } from '../pages/not-found-page'
import { SettingsPage } from '../pages/settings-page'
import { UserDetailPage } from '../pages/user-detail-page'
import { UsersPage } from '../pages/users-page'
import { RequireAuth } from './require-auth'
import { RequirePermission } from './require-permission'

export function AppRouter() {
  return (
    <div className="app-shell">
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
    </div>
  )
}
