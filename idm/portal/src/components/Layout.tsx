import { NavLink, Outlet } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { useAuth } from '../auth/AuthProvider'
import { api } from '../api/client'
import type { Me } from '../api/types'
import { t } from '../i18n'
import { BrandMark, Icon } from './Icon'
import { initials } from './ui'

function NavItem({ to, icon, label, soon }: { to: string; icon: string; label: string; soon?: boolean }) {
  if (soon) {
    return <span className="nav-link" aria-disabled="true" style={{ opacity: .6, cursor: 'default' }}><Icon name={icon} /> {label}<span className="soon">{t.nav.soon}</span></span>
  }
  return <NavLink to={to} end={to === '/'} className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}><Icon name={icon} /> {label}</NavLink>
}

export function Layout() {
  const { user, signOut } = useAuth()
  const me = useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/api/me') })
  const name = me.data?.name ?? (user.profile.name as string | undefined) ?? user.profile.sub

  return (
    <div className="shell">
      <header className="topbar">
        <div className="brand">
          <span className="mark"><BrandMark /></span>
          <span><div className="name">Bank IdM</div><div className="sub">{t.brandSub}</div></span>
        </div>
        <div className="spacer" />
        {import.meta.env.DEV && <span className="env-pill">{t.testEnv}</span>}
        <div className="user-chip">
          <span className="avatar">{initials(name)}</span>
          <span className="who">{name}</span>
        </div>
        <button className="ghost-btn" onClick={() => { void signOut() }}><Icon name="logout" size={18} /> {t.signOut}</button>
      </header>
      <nav className="sidebar" aria-label="Asosiy menyu">
        <div className="nav-section">{t.nav.overview}</div>
        <NavItem to="/" icon="dashboard" label={t.nav.dashboard} />
        <div className="nav-section">{t.nav.people}</div>
        <NavItem to="/identities" icon="people" label={t.nav.identities} />
        <div className="nav-section">{t.nav.access}</div>
        <NavItem to="/applications" icon="apps" label={t.nav.applications} />
        <NavItem to="/roles" icon="role" label={t.nav.roles} soon />
        <NavItem to="/requests" icon="request" label={t.nav.requests} soon />
        <NavItem to="/reviews" icon="review" label={t.nav.reviews} soon />
        <div className="nav-section">{t.nav.control}</div>
        <NavItem to="/audit" icon="audit" label={t.nav.audit} />
      </nav>
      <main className="main"><Outlet /></main>
    </div>
  )
}
