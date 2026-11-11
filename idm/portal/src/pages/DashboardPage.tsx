import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { useMe } from '../api/me'
import { useAuth } from '../auth/AuthProvider'
import type { AuditItem, ConnectorStatus, DashboardSummary, PagedResult } from '../api/types'
import { formatDate, formatDateTime, isQuietSync, t } from '../i18n'
import { Icon } from '../components/Icon'
import { ActivityFeed, Empty, ErrorBox, RunSummary, Stat } from '../components/ui'
import { ControlCenter, TrendFooter, useTrends } from './ControlCenter'

const dash = t.dashboard

export function DashboardPage() {
  const me = useMe()
  const { user } = useAuth()
  const summary = useQuery({
    queryKey: ['dashboard'],
    queryFn: () => api<DashboardSummary>('/api/dashboard'),
    refetchInterval: q => q.state.data?.connectors.some(c => c.isRunning) ? 3000 : 60_000,
  })
  const trends = useTrends(30)
  const levels = trends.data?.levels
  const audit = useQuery({ queryKey: ['audit', 'recent'], queryFn: () => api<PagedResult<AuditItem>>('/api/audit?take=40') })
  const d = summary.data
  const firstName = (me.data?.name ?? (user.profile.name as string | undefined) ?? '').split(' ')[0] ?? ''
  const now = new Date()
  const lastSync = d?.connectors.map(c => c.lastSuccessAt).filter((x): x is string => !!x).sort().at(-1) ?? null
  const failing = d?.connectors.filter(c => c.lastRun && (c.lastRun.status === 'Failed' || c.lastRun.status === 'Aborted')) ?? []

  return (
    <>
      <header className="brief">
        <div>
          <h1>{firstName ? t.greeting(firstName, now.getHours()) : dash.title}</h1>
          <p>{dash.subtitle}</p>
        </div>
        <dl className="brief-meta">
          <div><dt>{dash.weekday[now.getDay()]}</dt><dd>{formatDate(now.toISOString())}</dd></div>
          <div><dt>{dash.lastSync}</dt><dd>{lastSync ? t.ago(lastSync) : dash.never}</dd></div>
        </dl>
      </header>

      {summary.isError ? <div className="card" style={{ marginBottom: 16 }}><ErrorBox error={summary.error} onRetry={() => { void summary.refetch() }} /></div> : (
        <div className="overview">
          <section className="card queue" aria-label={dash.subtitle}>
            {d ? (
              <div className="insights">
                {d.leaversWithActiveAccounts > 0
                  ? <Insight to="/reports?tab=leavers" tone="bad" icon="warning" text={dash.insightLeavers(d.leaversWithActiveAccounts)} />
                  : <Insight tone="ok" icon="shield" text={dash.insightClean} />}
                {d.pendingRequests > 0 && <Insight to="/requests?tab=all" icon="request" text={dash.insightRequests(d.pendingRequests)} />}
                {d.sodViolations > 0 && <Insight to="/sod" tone="bad" icon="shield" text={dash.insightSod(d.sodViolations)} />}
                {d.orphanAccounts > 0 && <Insight to="/reports?tab=orphans" icon="user" text={dash.insightOrphans(d.orphanAccounts)} />}
                {failing.length > 0
                  ? <Insight to={`/applications/${encodeURIComponent(failing[0]!.code)}`} tone="bad" icon="sync" text={dash.insightSyncFailed(failing[0]!.name)} />
                  : d.connectors.length > 0 && <Insight tone="ok" icon="sync" text={dash.insightSyncOk(d.connectors.length)} />}
              </div>
            ) : <div className="card-body"><div className="skeleton" style={{ width: '60%' }} /></div>}
          </section>
          <div className="grid stats">
            <Stat label={dash.identities} value={d?.identities ?? '…'} to="/identities"
              hint={d ? `${d.activeIdentities} ${dash.active}, ${d.terminatedIdentities} ${dash.terminated}` : undefined} />
            <Stat label={dash.accounts} value={d?.accounts ?? '…'} to="/applications"
              hint={d ? `${d.applications} ${dash.systems}, ${dash.disabledHint(d.disabledAccounts)}` : undefined} />
            <Stat tone="bad" label={dash.leavers} value={d?.leaversWithActiveAccounts ?? '…'} hint={dash.leaversHint}
              attention={(d?.leaversWithActiveAccounts ?? 0) > 0} to="/reports?tab=leavers"
              trend={levels?.leavers_active && levels.leavers_active.length > 1 ? <TrendFooter series={levels.leavers_active} label={dash.leavers} /> : undefined} />
            <Stat tone="warn" label={dash.orphan} value={d?.orphanAccounts ?? '…'} hint={dash.orphanHint}
              attention={(d?.orphanAccounts ?? 0) > 0} to="/reports?tab=orphans"
              trend={levels?.orphan_accounts && levels.orphan_accounts.length > 1 ? <TrendFooter series={levels.orphan_accounts} label={dash.orphan} /> : undefined} />
          </div>
        </div>
      )}

      <ControlCenter />

      <div className="grid two-col">
        <section className="card">
          <div className="card-head"><h2>{dash.auditTitle}</h2><Link to="/audit">{t.nav.audit}<Icon name="chevron" size={14} /></Link></div>
          {audit.isError ? <ErrorBox error={audit.error} /> : audit.data?.items.length === 0 ? <Empty icon="audit" title={t.audit.emptyTitle} /> :
            audit.data ? <ActivityFeed items={audit.data.items.filter(e => !isQuietSync(e.action, e.details)).slice(0, 8)} /> : <div className="card-body"><div className="skeleton" style={{ width: '60%' }} /></div>}
        </section>
        <div className="grid">
          <section className="card">
            <div className="card-head"><h2>{dash.statusTitle}</h2><Link to="/identities">{t.nav.identities}<Icon name="chevron" size={14} /></Link></div>
            {d ? <StatusDistribution d={d} /> : <div className="card-body"><div className="skeleton" /></div>}
          </section>
          <section className="card">
            <div className="card-head"><h2>{dash.syncTitle}</h2><Link to="/applications">{t.nav.applications}<Icon name="chevron" size={14} /></Link></div>
            {d && d.connectors.length === 0 && <Empty icon="sync" title={dash.syncEmpty} text={dash.syncEmptyText} />}
            {d?.connectors.map(c => <ConnectorRow key={c.code} c={c} />)}
            <div className="card-body muted" style={{ fontSize: 12.5, borderTop: '1px solid var(--border)' }}>
              {dash.lastAudit}: {d?.lastAuditAt ? formatDateTime(d.lastAuditAt) : dash.never}
            </div>
          </section>
        </div>
      </div>
    </>
  )
}

/** Diqqat navbatining bir qatori: xavfli qatorlar (`bad`) CSS orqali doim tepada. Havola bo'lmasa — shunchaki holat. */
function Insight({ to, tone, icon, text }: { to?: string; tone?: 'bad' | 'ok'; icon: string; text: string }) {
  const body = <><Icon name={icon} size={18} /><span className="grow">{text}</span>{to && <span className="go"><Icon name="chevron" size={16} /></span>}</>
  const className = `insight${tone ? ` ${tone}` : ''}`
  return to ? <Link to={to} className={className}>{body}</Link> : <div className={className}>{body}</div>
}

function ConnectorRow({ c }: { c: ConnectorStatus }) {
  const hr = c.kind === 'HumanResources'
  return (
    <div className="connector-row">
      <span className="chip-icon" style={hr ? { ['--tone' as string]: 'var(--teal)', ['--tone-soft' as string]: 'var(--teal-soft)' } : undefined}>
        <Icon name={hr ? 'people' : 'apps'} size={15} />
      </span>
      <div className="grow">
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <Link to={`/applications/${encodeURIComponent(c.code)}`} className="cell-main">{c.name}</Link>
        </div>
        <div style={{ marginTop: 4 }}>{c.isRunning ? <span className="badge info live">{t.applications.syncing}</span> : <RunSummary run={c.lastRun} hr={hr} />}</div>
      </div>
    </div>
  )
}

/** Xodimlar holati: bitta yig'ma chiziq + legenda (rang yolg'iz emas: nom va son bilan). */
function StatusDistribution({ d }: { d: DashboardSummary }) {
  const pending = Math.max(0, d.identities - d.activeIdentities - d.suspendedIdentities - d.terminatedIdentities)
  const rows = [
    { key: 'Active', label: t.status.Active, value: d.activeIdentities, color: 'var(--ok)' },
    { key: 'Pending', label: t.status.Pending, value: pending, color: 'var(--sky)' },
    { key: 'Suspended', label: t.status.Suspended, value: d.suspendedIdentities, color: 'var(--warn)' },
    { key: 'Terminated', label: t.status.Terminated, value: d.terminatedIdentities, color: 'var(--faint)' },
  ]
  const total = d.identities || 1
  const pct = (v: number) => `${Math.round((v / total) * 100)}%`
  return (
    <div className="dist">
      <div className="dist-total"><b>{d.identities}</b><span>{dash.identitiesTotal}</span></div>
      <div className="stack" role="img" aria-label={rows.map(r => `${r.label}: ${r.value}`).join(', ')}>
        {rows.filter(r => r.value > 0).map(r => <span key={r.key} style={{ flex: r.value, background: r.color }} title={`${r.label}: ${r.value} (${pct(r.value)})`} />)}
      </div>
      <div className="legend">
        {rows.map(r => (
          <Link key={r.key} to={`/identities?status=${r.key}`} className="legend-row" style={{ color: 'inherit' }}>
            <i style={{ background: r.color }} /><span>{r.label}</span><b>{r.value}</b><small>{pct(r.value)}</small>
          </Link>
        ))}
      </div>
    </div>
  )
}
