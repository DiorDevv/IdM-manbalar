import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api, query } from '../api/client'
import { useMe } from '../api/me'
import type { PagedResult, RequestListItem, RequestStatus } from '../api/types'
import { formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Empty, ErrorBox, PageHead, Pager, Person, RiskBadge, SkeletonRows, Tabs, type TabDef } from '../components/ui'

const q = t.requests
const PAGE = 25
type Scope = 'inbox' | 'mine' | 'all'
const statusTone: Record<RequestStatus, string> = { Pending: 'warn', Approved: 'ok', Fulfilled: 'ok', Rejected: 'bad', Failed: 'bad', Cancelled: 'neutral' }

export function RequestStatusBadge({ status }: { status: RequestStatus }) {
  return <span className={`badge ${statusTone[status]}`}>{q.status[status]}</span>
}

export function RequestsPage() {
  const me = useMe()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const reader = !!me.data?.roles.some(r => r === 'idm.admin' || r === 'idm.auditor')
  const approver = reader || !!me.data?.hasTeam || (me.data?.inboxCount ?? 0) > 0
  const tabs: TabDef<Scope>[] = [
    ...(approver ? [{ id: 'inbox' as const, label: q.tabInbox, count: me.data?.inboxCount, bad: true }] : []),
    { id: 'mine', label: q.tabMine },
    ...(reader ? [{ id: 'all' as const, label: q.tabAll }] : []),
  ]
  const requested = params.get('tab') as Scope | null
  const scope: Scope = tabs.some(x => x.id === requested) ? requested! : (me.data?.inboxCount ? 'inbox' : 'mine')
  const [skip, setSkip] = useState(0)
  const list = useQuery({
    queryKey: ['requests', scope, skip],
    queryFn: () => api<PagedResult<RequestListItem>>(`/api/requests${query({ scope, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
    enabled: !!me.data,
  })
  const empty = scope === 'inbox' ? <Empty icon="review" title={q.emptyInbox} /> : scope === 'mine'
    ? <Empty icon="request" title={q.emptyMine} text={q.emptyMineText} /> : <Empty icon="request" title={q.emptyAll} />

  return (
    <>
      <PageHead title={q.title} subtitle={q.subtitle}
        actions={me.data?.identityId && <Link to="/requests/new" className="btn primary"><Icon name="sparkle" size={16} />{q.create}</Link>} />
      {me.data && !me.data.identityId && <div className="alert warn" style={{ marginBottom: 16 }}><Icon name="warning" size={18} /><span>{q.noIdentity}</span></div>}
      <section className="card">
        <Tabs tabs={tabs} current={scope} onSelect={x => { setSkip(0); setParams({ tab: x }, { replace: true }) }} />
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>{q.colRole}</th><th>{q.colFor}</th><th>{t.identities.colStatus}</th><th>{q.colStep}</th><th>{q.colCreated}</th></tr></thead>
              {list.isPending ? <SkeletonRows cols={5} /> : (
                <tbody>
                  {list.data?.items.map(r => (
                    <tr key={r.id} className="clickable" onClick={() => navigate(`/requests/${r.id}`)}>
                      <td>
                        <Link to={`/requests/${r.id}`} className="cell-main" onClick={e => e.stopPropagation()}>{r.roleName}</Link>
                        <div className="cell-sub" style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                          <span className="mono">{r.roleCode}</span><RiskBadge risk={r.risk} />
                          {r.hasSod && <span className="badge bad plain" title={q.sodWarning}><Icon name="shield" size={12} />SoD</span>}
                        </div>
                      </td>
                      <td><Person name={r.beneficiaryName} sub={<span className="mono">{r.beneficiaryNumber}</span>} /></td>
                      <td><RequestStatusBadge status={r.status} /></td>
                      <td>{r.currentStep ? <><div>{q.step[r.currentStep]}</div><div className="cell-sub">{r.currentApprover ?? q.adminPool}</div></> : <span className="muted">—</span>}</td>
                      <td style={{ whiteSpace: 'nowrap' }} title={formatDateTime(r.createdAt)}>{t.ago(r.createdAt)}<div className="cell-sub">{r.requesterName}</div></td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
            {list.data?.total === 0 && empty}
            {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />}
          </>
        )}
      </section>
    </>
  )
}
