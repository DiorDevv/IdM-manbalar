import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './auth/AuthProvider'
import { ApiError } from './api/client'
import { Layout } from './components/Layout'
import { DashboardPage } from './pages/DashboardPage'
import { IdentitiesPage } from './pages/IdentitiesPage'
import { IdentityDetailPage } from './pages/IdentityDetailPage'
import { ApplicationsPage } from './pages/ApplicationsPage'
import { ApplicationDetailPage } from './pages/ApplicationDetailPage'
import { AuditPage } from './pages/AuditPage'
import { ReportsPage } from './pages/ReportsPage'
import { EventsPage } from './pages/EventsPage'
import { RolesPage } from './pages/RolesPage'
import { RoleDetailPage } from './pages/RoleDetailPage'
import { SodPage } from './pages/SodPage'
import './styles.css'
import { initTheme } from './theme'

initTheme()

const queryClient = new QueryClient({
  defaultOptions: {
    // 401/403/404 qayta urinib bo'lmaydi; tarmoq xatolarida bir marta qayta urinish
    queries: { retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 1, refetchOnWindowFocus: false, staleTime: 15_000 },
  },
})

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route element={<Layout />}>
              <Route index element={<DashboardPage />} />
              <Route path="identities" element={<IdentitiesPage />} />
              <Route path="identities/:id" element={<IdentityDetailPage />} />
              <Route path="applications" element={<ApplicationsPage />} />
              <Route path="applications/:code" element={<ApplicationDetailPage />} />
              <Route path="events" element={<EventsPage />} />
              <Route path="reports" element={<ReportsPage />} />
              <Route path="roles" element={<RolesPage />} />
              <Route path="roles/:id" element={<RoleDetailPage />} />
              <Route path="sod" element={<SodPage />} />
              <Route path="audit" element={<AuditPage />} />
              <Route path="callback" element={<Navigate to="/" replace />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  </StrictMode>,
)
