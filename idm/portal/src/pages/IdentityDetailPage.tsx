import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { AccessState, AuditItem, IdentityAccess, IdentityDetails, PagedResult } from '../api/types'
import { formatDate, formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { ActivityFeed, Empty, ErrorBox, GroupChips, StatusBadge, avatarClass, initials } from '../components/ui'
import { Conflict } from './SodPage'

const stateTone: Record<AccessState, string> = { Granted: 'ok', Missing: 'warn', Excess: 'info' }

function Fact({ icon, label, children }: { icon: string; label: string; children: ReactNode }) {
  return <div className="fact"><div className="k"><Icon name={icon} size={14} />{label}</div><div className="v">{children}</div></div>
}

export function IdentityDetailPage() {
  const { id } = useParams()
  const q = useQuery({ queryKey: ['identity', id], queryFn: () => api<IdentityDetails>(`/api/identities/${id}`), enabled: !!id })
  const access = useQuery({ queryKey: ['identity-access', id], queryFn: () => api<IdentityAccess>(`/api/identities/${id}/access`), enabled: !!id })
  const history = useQuery({ queryKey: ['identity-history', id], queryFn: () => api<PagedResult<AuditItem>>(`/api/identities/${id}/history?take=20`), enabled: !!id })
  const crumbs = <><Link to="/identities">{t.identities.title}</Link><Icon name="chevron" size={13} /></>

  if (q.isError) {
    return <div className="card">{q.error instanceof ApiError && q.error.status === 404 ? <Empty title={t.identities.notFound} /> : <ErrorBox error={q.error} />}</div>
  }
  if (!q.data) return <div className="crumbs">{crumbs}{t.loading}</div>
  const i = q.data
  const name = [i.lastName, i.firstName, i.middleName].filter(Boolean).join(' ')
  const f = t.identities.fields
  const activeAccounts = i.accounts.filter(a => a.status === 'Enabled')
  const risky = i.status === 'Terminated' && activeAccounts.length > 0

  return (
    <>
      <div className="crumbs">{crumbs}{name}</div>

      {risky && (
        <div className="alert bad" style={{ marginBottom: 16 }}>
          <Icon name="warning" size={18} />
          <div className="grow"><strong>{t.identities.riskTitle}</strong><div style={{ marginTop: 2 }}>{t.identities.riskText(activeAccounts.map(a => `${a.applicationCode}: ${a.login}`).join(', '))}</div></div>
        </div>
      )}

      {access.data && access.data.sod.length > 0 && (
        <div className="alert bad" style={{ marginBottom: 16 }}>
          <Icon name="shield" size={18} />
          <div className="grow"><strong>{t.sod.identityAlert}</strong>
            {access.data.sod.map(v => <div key={v.ruleId} style={{ marginTop: 6 }}><div style={{ marginBottom: 4 }}>{v.ruleName} ({v.ruleCode})</div><Conflict left={v.left} right={v.right} /></div>)}
          </div>
        </div>
      )}

      <section className="card profile-card">
        <div className={`cover${i.status === 'Terminated' ? ' terminated' : ''}`} />
        <div className="profile-main">
          <div className={`avatar-xl avatar ${avatarClass(`${i.lastName} ${i.firstName}`)}`}>{initials(`${i.lastName} ${i.firstName}`)}</div>
          <div style={{ paddingBottom: 2 }}>
            <h1>{name}</h1>
            <div className="meta">
              <StatusBadge status={i.status} />
              <span className="badge neutral plain">{t.source[i.source]}</span>
              <span className="mono">{i.employeeNumber}</span>
              {i.jobTitle && <span>· {i.jobTitle}</span>}
            </div>
          </div>
        </div>
        <div className="facts">
          <Fact icon="mail" label={f.email}>{i.email ?? '—'}</Fact>
          <Fact icon="building" label={f.department}>{i.department ?? '—'}</Fact>
          <Fact icon="briefcase" label={f.jobTitle}>{i.jobTitle ?? '—'}</Fact>
          <Fact icon="user" label={f.manager}>{i.managerId ? <Link to={`/identities/${i.managerId}`}>{i.managerName}</Link> : '—'}</Fact>
          <Fact icon="calendar" label={f.hireDate}>{formatDate(i.hireDate)}</Fact>
          <Fact icon="calendar" label={f.terminationDate}>{formatDate(i.terminationDate)}</Fact>
        </div>
      </section>

      <section className="card" style={{ marginBottom: 16 }}>
        <div className="card-head">
          <h2>{t.roles.accessTitle}</h2>
          {access.data && <div className="chips">
            {access.data.roles.length === 0 ? <span className="muted" style={{ fontSize: 12.5 }}>{t.roles.noRoles}</span> : access.data.roles.map(r => (
              <Link key={`${r.roleId}-${r.source}`} to={`/roles/${r.roleId}`} className="chip" title={t.roles.source[r.source]}>
                <Icon name="role" size={12} />{r.name}<span className="muted">· {t.roles.source[r.source]}</span>
              </Link>
            ))}
          </div>}
        </div>
        {access.isError ? <ErrorBox error={access.error} /> : !access.data ? <div className="card-body"><div className="skeleton" /></div> :
          access.data.lines.length === 0 ? <Empty icon="lock" title={t.roles.noAccess} /> : (
            <table className="data">
              <thead><tr><th>{t.roles.colEntitlements}</th><th>{t.identities.colStatus}</th><th>{t.roles.via}</th><th>Akkaunt</th></tr></thead>
              <tbody>
                {access.data.lines.map(l => (
                  <tr key={l.entitlementId}>
                    <td><div className="cell-main" style={{ display: 'flex', gap: 6, alignItems: 'center' }}>{l.isPrivileged && <span style={{ color: 'var(--warn)' }}><Icon name="lock" size={13} /></span>}{l.entitlementName}</div>
                      <div className="cell-sub">{l.applicationCode}</div></td>
                    <td><span className={`badge ${stateTone[l.state]}`} title={t.roles.stateHelp[l.state]}>{t.roles[l.state === 'Granted' ? 'granted' : l.state === 'Missing' ? 'missing' : 'excess']}</span></td>
                    <td className="cell-sub">{l.viaRoles.join(', ') || '—'}</td>
                    <td className="mono">{l.accounts.join(', ') || '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
      </section>

      <div className="grid two-col">
        <section className="card">
          <div className="card-head"><h2>{t.identities.accounts}</h2><span className="muted" style={{ fontSize: 12.5 }}>{t.identities.accountsCount(activeAccounts.length, i.accounts.length)}</span></div>
          {i.accounts.length === 0 ? <Empty icon="apps" title={t.identities.noAccounts} /> : (
            <table className="data">
              <thead><tr><th>{t.applications.colName}</th><th>Login</th><th>{t.identities.colStatus}</th><th>{t.identities.groups}</th></tr></thead>
              <tbody>
                {i.accounts.map(a => (
                  <tr key={a.id}>
                    <td><Link to={`/applications/${encodeURIComponent(a.applicationCode)}`} className="cell-main">{a.applicationName}</Link>
                      <div className="cell-sub">{t.identities.synced} {t.ago(a.lastSyncedAt)}</div></td>
                    <td className="mono">{a.login}</td>
                    <td><StatusBadge status={a.status} /></td>
                    <td><GroupChips groups={a.entitlements} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <section className="card">
          <div className="card-head"><h2>{t.identities.history}</h2><Link to="/audit">{t.nav.audit}<Icon name="chevron" size={14} /></Link></div>
          {history.isError ? <ErrorBox error={history.error} /> : history.data?.items.length === 0 ? <Empty icon="clock" title={t.identities.noHistory} /> :
            history.data ? <ActivityFeed items={history.data.items} /> : <div className="card-body"><div className="skeleton" /></div>}
          <div className="card-body muted" style={{ fontSize: 12, borderTop: '1px solid var(--border)' }}>
            {f.created}: {formatDateTime(i.createdAt)} · {f.updated}: {formatDateTime(i.updatedAt)}
          </div>
        </section>
      </div>
    </>
  )
}
