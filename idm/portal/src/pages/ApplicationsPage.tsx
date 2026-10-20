import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ApplicationItem } from '../api/types'
import { t } from '../i18n'
import { Empty, ErrorBox, PageHead, RiskBadge, SkeletonRows } from '../components/ui'

export function ApplicationsPage() {
  const q = useQuery({ queryKey: ['applications'], queryFn: () => api<ApplicationItem[]>('/api/applications') })
  return (
    <>
      <PageHead title={t.applications.title} subtitle={t.applications.subtitle} />
      <section className="card">
        {q.isError ? <ErrorBox error={q.error} onRetry={() => { void q.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>{t.applications.colName}</th><th>{t.applications.colKind}</th><th>{t.applications.colRisk}</th><th style={{ textAlign: 'right' }}>{t.applications.colAccounts}</th><th style={{ textAlign: 'right' }}>{t.applications.colEntitlements}</th><th>{t.applications.colStatus}</th></tr></thead>
              {q.isPending ? <SkeletonRows cols={6} rows={3} /> : (
                <tbody>
                  {q.data?.map(a => (
                    <tr key={a.id}>
                      <td><div className="cell-main">{a.name}</div><div className="cell-sub">{a.code}{a.description ? ` · ${a.description}` : ''}</div></td>
                      <td>{a.kind}</td>
                      <td><RiskBadge risk={a.risk} /></td>
                      <td style={{ textAlign: 'right' }}>{a.accountCount}</td>
                      <td style={{ textAlign: 'right' }}>{a.entitlementCount}</td>
                      <td><span className={`badge ${a.isActive ? 'ok' : 'neutral'}`}>{a.isActive ? t.status.Active : t.status.Disabled}</span></td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
            {q.data?.length === 0 && <Empty icon="apps" title={t.applications.emptyTitle} text={t.applications.emptyText} />}
          </>
        )}
      </section>
    </>
  )
}
