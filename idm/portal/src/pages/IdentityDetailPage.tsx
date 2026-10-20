import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { IdentityDetails } from '../api/types'
import { formatDate, formatDateTime, t } from '../i18n'
import { Empty, ErrorBox, StatusBadge, initials } from '../components/ui'

export function IdentityDetailPage() {
  const { id } = useParams()
  const q = useQuery({ queryKey: ['identity', id], queryFn: () => api<IdentityDetails>(`/api/identities/${id}`), enabled: !!id })
  const crumbs = <><Link to="/identities">{t.identities.title}</Link> / </>

  if (q.isError) {
    return <div className="card">{q.error instanceof ApiError && q.error.status === 404 ? <Empty title={t.identities.notFound} /> : <ErrorBox error={q.error} />}</div>
  }
  if (!q.data) return <div className="crumbs">{crumbs}{t.loading}</div>
  const i = q.data
  const name = [i.lastName, i.firstName, i.middleName].filter(Boolean).join(' ')
  const f = t.identities.fields

  return (
    <>
      <div className="crumbs">{crumbs}{name}</div>
      <div className="profile" style={{ marginBottom: 22 }}>
        <div className="avatar-lg">{initials(`${i.lastName} ${i.firstName}`)}</div>
        <div>
          <h1>{name}</h1>
          <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
            <StatusBadge status={i.status} />
            <span className="badge neutral">{t.source[i.source]}</span>
            <span className="mono" style={{ color: 'var(--muted)' }}>{i.employeeNumber}</span>
          </div>
        </div>
      </div>
      <div className="grid two-col">
        <section className="card">
          <div className="card-head"><h2>{t.identities.accounts}</h2></div>
          {i.accounts.length === 0 ? <Empty icon="apps" title={t.identities.noAccounts} /> : (
            <table className="data">
              <thead><tr><th>{t.applications.colName}</th><th>Login</th><th>{t.identities.colStatus}</th><th>{f.updated}</th></tr></thead>
              <tbody>
                {i.accounts.map(a => (
                  <tr key={a.id}>
                    <td><div className="cell-main">{a.applicationName}</div><div className="cell-sub">{a.applicationCode}</div></td>
                    <td className="mono">{a.login}</td>
                    <td><StatusBadge status={a.status} /></td>
                    <td>{formatDateTime(a.lastSyncedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <section className="card">
          <div className="card-head"><h2>{t.identities.details}</h2></div>
          <dl className="kv card-body">
            <dt>{f.email}</dt><dd>{i.email ?? '—'}</dd>
            <dt>{f.department}</dt><dd>{i.department ?? '—'}</dd>
            <dt>{f.jobTitle}</dt><dd>{i.jobTitle ?? '—'}</dd>
            <dt>{f.manager}</dt><dd>{i.managerId ? <Link to={`/identities/${i.managerId}`}>{i.managerName}</Link> : '—'}</dd>
            <dt>{f.hireDate}</dt><dd>{formatDate(i.hireDate)}</dd>
            <dt>{f.terminationDate}</dt><dd>{formatDate(i.terminationDate)}</dd>
            <dt>{f.created}</dt><dd>{formatDateTime(i.createdAt)}</dd>
            <dt>{f.updated}</dt><dd>{formatDateTime(i.updatedAt)}</dd>
          </dl>
        </section>
      </div>
    </>
  )
}
