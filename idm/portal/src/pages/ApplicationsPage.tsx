import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { ApplicationItem } from '../api/types'
import { t } from '../i18n'
import { Empty, ErrorBox, PageHead, RiskBadge, RunSummary, SkeletonRows } from '../components/ui'

export function ApplicationsPage() {
  const navigate = useNavigate()
  const q = useQuery({
    queryKey: ['applications'],
    queryFn: () => api<ApplicationItem[]>('/api/applications'),
    // sinxronlash ketayotgan bo'lsa natijani kutib yangilab turamiz
    refetchInterval: query => query.state.data?.some(a => a.isSyncing) ? 3000 : false,
  })
  return (
    <>
      <PageHead title={t.applications.title} subtitle={t.applications.subtitle} />
      <section className="card">
        {q.isError ? <ErrorBox error={q.error} onRetry={() => { void q.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>{t.applications.colName}</th><th>{t.applications.colKind}</th><th>{t.applications.colRisk}</th><th style={{ textAlign: 'right' }}>{t.applications.colAccounts}</th><th style={{ textAlign: 'right' }}>{t.applications.colEntitlements}</th><th>{t.applications.colLastSync}</th></tr></thead>
              {q.isPending ? <SkeletonRows cols={6} rows={3} /> : (
                <tbody>
                  {q.data?.map(a => (
                    <tr key={a.id} className="clickable" onClick={() => navigate(`/applications/${encodeURIComponent(a.code)}`)}>
                      <td><div className="cell-main">{a.name}</div><div className="cell-sub">{a.code}{a.description ? ` · ${a.description}` : ''}</div></td>
                      <td>{t.kind[a.kind] ?? a.kind}</td>
                      <td><RiskBadge risk={a.risk} /></td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{a.accountCount}</td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{a.entitlementCount}</td>
                      <td style={{ maxWidth: 360 }}>
                        {a.isSyncing ? <span className="badge info live">{t.applications.syncing}</span>
                          : a.hasConnector || a.lastRun ? <RunSummary run={a.lastRun} hr={a.kind === 'HumanResources'} /> : <span style={{ color: 'var(--muted)', fontSize: 13 }}>{t.applications.noConnector}</span>}
                      </td>
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
