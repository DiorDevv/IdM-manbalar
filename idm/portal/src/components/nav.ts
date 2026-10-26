import { t } from '../i18n'

/** Menyu bo'limlari: yon panel va ⌘K palitrasi shu ro'yxatdan quriladi. */
/** everyone — har bir xodim (ariza berish va tasdiqlash); qolganlari faqat administrator va auditor uchun. */
export interface NavEntry { to: string; icon: string; label: string; soon?: boolean; everyone?: boolean }
export interface NavSection { title: string; items: NavEntry[] }

export const NAV: NavSection[] = [
  { title: t.nav.overview, items: [{ to: '/', icon: 'dashboard', label: t.nav.dashboard }] },
  {
    title: t.nav.people, items: [
      { to: '/identities', icon: 'people', label: t.nav.identities },
      { to: '/events', icon: 'events', label: t.nav.events },
    ],
  },
  {
    title: t.nav.access, items: [
      { to: '/applications', icon: 'apps', label: t.nav.applications },
      { to: '/roles', icon: 'role', label: t.nav.roles },
      { to: '/requests', icon: 'request', label: t.nav.requests, everyone: true },
      { to: '/reviews', icon: 'review', label: t.nav.reviews, soon: true },
    ],
  },
  {
    title: t.nav.control, items: [
      { to: '/reports', icon: 'reports', label: t.nav.reports },
      { to: '/sod', icon: 'shield', label: t.nav.sod },
      { to: '/audit', icon: 'audit', label: t.nav.audit },
    ],
  },
]
