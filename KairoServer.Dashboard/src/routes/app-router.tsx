import { Route, Routes } from 'react-router-dom'
import { AdminLayout } from '../layouts/admin-layout'
import { DashboardPage } from '../pages/dashboard-page'
import { DeliveriesPage } from '../pages/deliveries-page'
import { LoginPage } from '../pages/login-page'
import { NotFoundPage } from '../pages/not-found-page'
import { SettingsPage } from '../pages/settings-page'
import { RequireAuth } from './require-auth'

export function AppRouter() {
  return (
    <div className="app-shell">
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<RequireAuth />}>
          <Route element={<AdminLayout />}>
            <Route index element={<DashboardPage />} />
            <Route path="deliveries" element={<DeliveriesPage />} />
            <Route path="settings" element={<SettingsPage />} />
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Route>
        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </div>
  )
}
