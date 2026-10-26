import { useDeferredValue, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError, query } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { CampaignDetails, PagedResult, ReviewDecision, ReviewItem } from '../api/types'
import { formatDate, formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { ExportButtons } from '../components/ExportButtons'
import { ConfirmDialog, Empty, ErrorBox, PageHead, Pager, Person, SkeletonRows, Stat } from '../components/ui'
import { isOverdue } from './ReviewsPage'

const v = t.reviews
const PAGE = 50
const decisionTone: Record<ReviewDecision, string> = { Pending: 'warn', Keep: 'ok', Revoke: 'bad' }

export function CampaignPage() {
  const { id = '' } = useParams()
  const isAdmin = useIsAdmin()
  const client = useQueryClient()
  const [decision, setDecision] = useState<ReviewDecision | ''>('')
  const [search, setSearch] = useState('')
  const deferred = useDeferredValue(search.trim())
  const [skip, setSkip] = useState(0)
  const [closing, setClosing] = useState<'complete' | 'cancel' | null>(null)
  const details = useQuery({ queryKey: ['reviews', 'campaign', id], queryFn: () => api<CampaignDetails>(`/api/reviews/campaigns/${id}`) })
  const items = useQuery({
    queryKey: ['reviews', 'campaign', id, 'items', decision, deferred, skip],
    queryFn: () => api<PagedResult<ReviewItem>>(`/api/reviews/campaigns/${id}/items${query({ decision, search: deferred, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  const close = useMutation({
    mutationFn: (cancel: boolean) => api(`/api/reviews/campaigns/${id}/close`, { method: 'POST', body: JSON.stringify({ cancel }) }),
    onSettled: () => client.invalidateQueries({ queryKey: ['reviews'] }),
  })

  const crumbs = <><Link to="/reviews?tab=campaigns">{v.title}</Link><Icon name="chevron" size={13} /></>
  if (details.isError) {
    return <div className="card">{details.error instanceof ApiError && details.error.status === 404 ? <Empty icon="review" title={v.notFound} /> : <ErrorBox error={details.error} />}</div>
  }
  if (!details.data) return <div className="crumbs">{crumbs}{t.loading}</div>
  const c = details.data.campaign
  const decided = c.total - c.pending
  const active = c.status === 'Active'

  return (
    <>
      <PageHead crumbs={<>{crumbs}{c.name}</>} title={c.name}
        subtitle={<span style={{ display: 'inline-flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          <span className={`badge ${active ? 'info' : c.status === 'Completed' ? 'ok' : 'neutral'}`}>{v.status[c.status]}</span>
          {[c.department ?? v.scopeAll, c.applicationCode, c.privilegedOnly ? v.privilegedOnly : null].filter(Boolean).join(' · ')}
          <span>· {v.due}: {formatDate(c.dueDate)}{active && isOverdue(c.dueDate) && <b style={{ color: 'var(--bad)' }}> ({v.overdue})</b>}</span>
          <span>· {v.createdBy}: {c.createdByName ?? c.createdBy}</span>
        </span>}
        actions={<>
          <ExportButtons report="campaign" campaignId={id} />
          {isAdmin && active && <>
            <button className="btn ghost sm" onClick={() => setClosing('cancel')}>{v.cancelCampaign}</button>
            <button className="btn primary sm" onClick={() => setClosing('complete')}><Icon name="check" size={15} />{v.complete}</button>
          </>}
        </>} />
      {c.description && <p className="muted" style={{ marginTop: -8 }}>{c.description}</p>}
      {close.isError && <div className="card" style={{ marginBottom: 16 }}><ErrorBox error={close.error} /></div>}

      <div className="grid stats">
        <Stat icon="review" label={v.statTotal} value={c.total} tone="accent" />
        <Stat icon="check" label={v.statDecided} value={v.progress(decided, c.total)} tone="ok" hint={
          <div className={`progress${c.pending === 0 ? ' done' : ''}`}><span style={{ width: `${c.total ? (decided * 100) / c.total : 0}%` }} /></div>} />
        <Stat icon="x" label={v.statRevoked} value={c.revoked} tone="bad" />
        <Stat icon="clock" label={v.statOpen} value={c.revokedOpen} tone="warn" attention={c.revokedOpen > 0} to="/provisioning" />
      </div>

      <div className="two-col" style={{ alignItems: 'start' }}>
        <section className="card">
          <div className="toolbar">
            <label className="search"><Icon name="search" size={18} />
              <input className="input" placeholder={v.search} aria-label={v.search} value={search} onChange={e => { setSearch(e.target.value); setSkip(0) }} />
            </label>
            <div className="segmented" role="group">
              {(['', 'Pending', 'Keep', 'Revoke'] as const).map(d => (
                <button key={d} aria-pressed={decision === d} onClick={() => { setDecision(d); setSkip(0) }}>{d ? v.decision[d] : v.allDecisions}</button>
              ))}
            </div>
          </div>
          {items.isError ? <ErrorBox error={items.error} onRetry={() => { void items.refetch() }} /> : (
            <>
              <div className="table-wrap">
                <table className="data">
                  <thead><tr><th>{t.provisioning.colWho}</th><th>{v.colName}</th><th>{v.reviewer}</th><th>{v.decidedBy}</th></tr></thead>
                  {items.isPending ? <SkeletonRows cols={4} /> : (
                    <tbody>
                      {items.data.items.map(i => (
                        <tr key={i.id}>
                          <td><Person name={i.identityName} to={`/identities/${i.identityId}`} sub={<span className="mono">{i.employeeNumber}</span>} /></td>
                          <td>
                            <div className="cell-main" style={{ display: 'flex', gap: 6, alignItems: 'center' }}>{i.isPrivileged && <Icon name="lock" size={14} />}{i.title}</div>
                            <div className="cell-sub">{v.kind[i.kind]} · <span className="mono">{i.subtitle}</span> · {i.applicationCode}</div>
                          </td>
                          <td>{i.reviewerName ?? <span className="muted">{v.adminPool}</span>}</td>
                          <td>
                            <span className={`badge ${decisionTone[i.decision]}`}>{v.decision[i.decision]}</span>
                            {i.decidedAt && <div className="cell-sub" title={formatDateTime(i.decidedAt)}>{i.decidedByName ?? i.decidedBy} · {formatDate(i.decidedAt)}</div>}
                            {i.comment && <div className="cell-sub">«{i.comment}»</div>}
                            {i.decision === 'Revoke' && <div className="cell-sub">{i.remediatedAt ? v.remediated : v.remediationPending}</div>}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  )}
                </table>
              </div>
              {items.data?.total === 0 && <Empty icon="review" title={t.exports.empty} />}
              {items.data && <Pager skip={skip} page={PAGE} total={items.data.total} onChange={setSkip} />}
            </>
          )}
        </section>

        <section className="card">
          <div className="card-head"><h2>{v.reviewers}</h2></div>
          {details.data.reviewers.map(r => (
            <div key={r.reviewerId ?? 'admins'} className="list-row">
              <div className="grow">
                {r.reviewerName ? <Person name={r.reviewerName} /> : <span className="muted">{v.adminPool}</span>}
                <div className={`progress${r.pending === 0 ? ' done' : ''}`} style={{ marginTop: 6 }}>
                  <span style={{ width: `${r.total ? ((r.total - r.pending) * 100) / r.total : 0}%` }} />
                </div>
              </div>
              <span className={`badge ${r.pending === 0 ? 'ok' : 'warn'}`}>{v.pendingOf(r.pending, r.total)}</span>
            </div>
          ))}
        </section>
      </div>

      <ConfirmDialog open={closing !== null} title={closing === 'cancel' ? v.cancelCampaign : v.complete} danger={closing === 'cancel'}
        text={closing === 'cancel' ? v.cancelText : v.completeText} confirmLabel={closing === 'cancel' ? v.cancelCampaign : v.complete}
        onCancel={() => setClosing(null)} onConfirm={() => { close.mutate(closing === 'cancel'); setClosing(null) }} />
    </>
  )
}
