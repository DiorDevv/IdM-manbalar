import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { AuditItem, DashboardSummary, PagedResult } from '../api/types'
import { formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Empty, ErrorBox, PageHead } from '../components/ui'

function Stat({ icon, label, value, hint, attention }: { icon: string; label: string; value: number | string; hint?: string; attention?: boolean }) {
  return (
    <div className={`card stat${attention ? ' attention' : ''}`}>
      <div className="label"><Icon name={icon} size={18} /> {label}</div>
      <div className="value">{value}</div>
      {hint && <div className="hint">{hint}</div>}
    </div>
  )
}

export function DashboardPage() {
  const summary = useQuery({ queryKey: ['dashboard'], queryFn: () => api<DashboardSummary>('/api/dashboard') })
  const audit = useQuery({ queryKey: ['audit', 'recent'], queryFn: () => api<PagedResult<AuditItem>>('/api/audit?take=6') })
  const d = summary.data

  return (
    <>
      <PageHead title={t.dashboard.title} subtitle={t.dashboard.subtitle} />
      {summary.isError ? <div className="card"><ErrorBox error={summary.error} onRetry={() => { void summary.refetch() }} /></div> : (
        <div className="grid stats">
          <Stat icon="people" label={t.dashboard.identities} value={d?.identities ?? '…'}
            hint={d ? `${d.activeIdentities} ${t.dashboard.active} · ${d.suspendedIdentities} ${t.dashboard.suspended} · ${d.terminatedIdentities} ${t.dashboard.terminated}` : undefined} />
          <Stat icon="apps" label={t.dashboard.applications} value={d?.applications ?? '…'} />
          <Stat icon="shield" label={t.dashboard.accounts} value={d?.accounts ?? '…'} />
          <Stat icon="warning" label={t.dashboard.orphan} value={d?.orphanAccounts ?? '…'} hint={t.dashboard.orphanHint} attention={(d?.orphanAccounts ?? 0) > 0} />
          <Stat icon="request" label={t.dashboard.pending} value={d?.pendingRequests ?? '…'} />
        </div>
      )}
      <div className="grid two-col">
        <section className="card">
          <div className="card-head"><h2>{t.dashboard.auditTitle}</h2><Link to="/audit">{t.nav.audit} →</Link></div>
          {audit.isError ? <ErrorBox error={audit.error} /> : audit.data?.items.length === 0 ? <Empty icon="audit" title={t.audit.emptyTitle} /> : (
            audit.data?.items.map(e => (
              <div className="timeline-item" key={e.id}>
                <time>{formatDateTime(e.occurredAt)}</time>
                <div><span className="action-code">{e.action}</span> <span style={{ color: 'var(--muted)' }}>· {e.actorName ?? e.actorId}</span></div>
              </div>
            ))
          )}
        </section>
        <section className="card">
          <div className="card-head"><h2>{t.dashboard.nextTitle}</h2></div>
          <div className="card-body" style={{ color: 'var(--ink-2)', fontSize: 14, lineHeight: 1.6 }}>
            <p style={{ marginTop: 0 }}>{t.dashboard.nextText}</p>
            <p style={{ marginBottom: 0, color: 'var(--muted)' }}>{t.dashboard.lastAudit}: {d?.lastAuditAt ? formatDateTime(d.lastAuditAt) : t.dashboard.never}</p>
          </div>
        </section>
      </div>
    </>
  )
}
