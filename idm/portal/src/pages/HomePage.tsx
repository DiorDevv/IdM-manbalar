import { Navigate } from 'react-router-dom'
import { useMe } from '../api/me'
import { DashboardPage } from './DashboardPage'

/** Bosh sahifa: administrator va auditor — umumiy holat; oddiy xodim — o'z arizalari (ma'lumotlarni ko'rish huquqi yo'q). */
export function HomePage() {
  const me = useMe()
  if (!me.data) return null
  return me.data.roles.some(r => r === 'idm.admin' || r === 'idm.auditor') ? <DashboardPage /> : <Navigate to="/requests" replace />
}
