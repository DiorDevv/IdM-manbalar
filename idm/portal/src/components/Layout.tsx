import { signInWithMfa } from '../auth/oidc'
import { useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { DashboardSummary } from '../api/types'
import { useAuth } from '../auth/AuthProvider'
import { useMe } from '../api/me'
import { t } from '../i18n'
import { nextTheme, setTheme, useTheme } from '../theme'
import { CommandPalette } from './CommandPalette'
import { BrandMark, Icon } from './Icon'
import { NAV, type NavEntry } from './nav'
import { Avatar } from './ui'

function NavItem({ item, count }: { item: NavEntry; count?: number }) {
  if (item.soon) {
    return <span className="nav-link" aria-disabled="true"><Icon name={item.icon} /> {item.label}<span className="soon">{t.nav.soon}</span></span>
  }
  return <NavLink to={item.to} end={item.to === '/'} className={({ isActive }) => `nav-link${isActive ? ' active' : ''}`}>
    <Icon name={item.icon} /> {item.label}{!!count && <span className="count" title={t.dashboard.leavers}>{count}</span>}
  </NavLink>
}

const themeIcon = { system: 'monitor', light: 'sun', dark: 'moon' } as const

export function Layout() {
  const { user, signOut } = useAuth()
  const me = useMe()
  const theme = useTheme()
  const location = useLocation()
  const [navOpen, setNavOpen] = useState(false)
  const [cmdOpen, setCmdOpen] = useState(false)
  const name = me.data?.name ?? (user.profile.name as string | undefined) ?? user.profile.sub
  const roles = me.data?.roles ?? []
  const canSearch = roles.includes('idm.admin') || roles.includes('idm.auditor')
  // Xavf hisoblagichi (menyu va qo'ng'iroq): bosh sahifa bilan bitta so'rov, faqat o'qish huquqi bo'lsa
  const dashboard = useQuery({ queryKey: ['dashboard'], queryFn: () => api<DashboardSummary>('/api/dashboard'), enabled: canSearch, refetchInterval: 60_000 })
  const risks = dashboard.data?.leaversWithActiveAccounts ?? 0
  const current = NAV.flatMap(s => s.items).filter(i => !i.soon && (i.to === '/' ? location.pathname === '/' : location.pathname.startsWith(i.to)))[0]

  useEffect(() => { setNavOpen(false) }, [location.pathname])
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setCmdOpen(o => !o) }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])

  const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform)
  return (
    <div className={`app${navOpen ? ' nav-open' : ''}`} onClick={e => { if (navOpen && e.target === e.currentTarget) setNavOpen(false) }}>
      <nav className="sidebar" aria-label={t.nav.label}>
        <div className="brand">
          <span className="mark"><BrandMark /></span>
          <span><div className="name">Bank IdM</div><div className="sub">{t.brandSub}</div></span>
        </div>
        {NAV.map(section => ({ ...section, items: section.items.filter(i => canSearch || i.everyone) })).filter(s => s.items.length > 0).map(section => (
          <div key={section.title}>
            <div className="nav-section">{section.title}</div>
            {section.items.map(item => <NavItem key={item.to} item={item} count={item.to === '/reports' ? risks : item.to === '/sod' ? dashboard.data?.sodViolations
              : item.to === '/requests' ? me.data?.inboxCount : item.to === '/reviews' ? me.data?.reviewCount : undefined} />)}
          </div>
        ))}
        <div className="sidebar-foot">
          {import.meta.env.DEV && <span className="env-pill">{t.testEnv}</span>}
          <div className="user-card">
            <Avatar name={name} />
            <span className="who"><b>{name}</b><span>{roles.length ? roles.map(r => t.portalRoles[r as keyof typeof t.portalRoles] ?? r).join(', ') : t.portalRoles.employee}</span></span>
            <button className="icon-btn" onClick={() => { void signOut() }} aria-label={t.signOut} title={t.signOut}><Icon name="logout" size={17} /></button>
          </div>
        </div>
      </nav>
      <div className="main-col">
        <header className="header">
          <button className="icon-btn menu-btn" onClick={() => setNavOpen(true)} aria-label={t.nav.open}><Icon name="menu" /></button>
          {current && <span className="where"><Icon name={current.icon} size={16} />{current.label}</span>}
          <button className="cmd-trigger" onClick={() => setCmdOpen(true)} aria-label={t.cmd.title}>
            <Icon name="search" size={15} /><span className="grow">{t.cmd.trigger}</span><span className="kbd">{isMac ? '⌘' : 'Ctrl'} K</span>
          </button>
          <div className="spacer" />
          {canSearch && (
            <Link to="/reports?tab=leavers" className={`icon-btn${risks ? ' has-dot' : ''}`} aria-label={risks ? `${t.dashboard.leavers}: ${risks}` : t.nav.reports}
              title={risks ? `${t.dashboard.leavers}: ${risks}` : t.reports.allGood}>
              <Icon name="bell" size={17} />
            </Link>
          )}
          <button className="icon-btn" onClick={() => setTheme(nextTheme(theme))} aria-label={t.theme.label} title={`${t.theme.label}: ${t.theme[theme]}`}>
            <Icon name={themeIcon[theme]} size={17} />
          </button>
        </header>
        <main className="content" key={location.pathname}>
          {!!me.data?.mfaRequiredFor?.length && (
            <div className="alert warn no-print" role="alert" style={{ marginBottom: 16 }}>
              <Icon name="lock" size={18} />
              <span className="grow">{t.mfaRequired(me.data.mfaRequiredFor.map(r => t.portalRoles[r as keyof typeof t.portalRoles] ?? r).join(', '))}</span>
              {me.data.mfaStepUpAcr && (
                <button className="btn secondary sm" onClick={() => { void signInWithMfa(me.data!.mfaStepUpAcr!) }}>{t.mfaStepUp}</button>
              )}
            </div>
          )}
          <Outlet />
        </main>
      </div>
      <CommandPalette open={cmdOpen} onClose={() => setCmdOpen(false)} canSearch={canSearch} />
    </div>
  )
}
