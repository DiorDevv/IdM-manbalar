import { useState, useDeferredValue } from 'react'
import { useMutation, useQuery, keepPreviousData } from '@tanstack/react-query'
import { api, query } from '../api/client'
import type { AuditItem, AuditVerification, PagedResult } from '../api/types'
import { describeAction, formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Empty, ErrorBox, PageHead, SkeletonRows } from '../components/ui'

const PAGE = 50

export function AuditPage() {
  const [action, setAction] = useState('')
  const [skip, setSkip] = useState(0)
  const deferred = useDeferredValue(action)
  const list = useQuery({
    queryKey: ['audit', deferred, skip],
    queryFn: () => api<PagedResult<AuditItem>>(`/api/audit${query({ action: deferred, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  const verify = useMutation({ mutationFn: () => api<AuditVerification>('/api/audit/verify') })

  return (
    <>
      <PageHead title={t.audit.title} subtitle={t.audit.subtitle}
        actions={<button className="btn secondary" onClick={() => verify.mutate()} disabled={verify.isPending}><Icon name="shield" size={18} />{verify.isPending ? t.audit.verifying : t.audit.verify}</button>} />
      {verify.data && (
        <div className={`alert ${verify.data.isValid ? 'ok' : 'bad'}`} style={{ marginBottom: 16 }}>
          <Icon name={verify.data.isValid ? 'shield' : 'warning'} size={18} />
          <span>{verify.data.isValid ? t.audit.valid(verify.data.checkedCount) : t.audit.broken(verify.data.firstBrokenId ?? 0)}</span>
        </div>
      )}
      {verify.isError && <div style={{ marginBottom: 16 }}><ErrorBox error={verify.error} /></div>}
      <section className="card">
        <div className="toolbar">
          <label className="search"><Icon name="search" size={18} /><input className="input" placeholder={t.audit.filterAction} value={action} onChange={e => { setAction(e.target.value); setSkip(0) }} /></label>
        </div>
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>#</th><th>{t.identities.fields.created}</th><th>{t.audit.actor}</th><th>Amal</th><th>{t.audit.target}</th><th>Tafsilot</th></tr></thead>
              {list.isPending ? <SkeletonRows cols={6} /> : (
                <tbody>
                  {list.data?.items.map(e => (
                    <tr key={e.id}>
                      <td className="mono" style={{ color: 'var(--muted)' }}>{e.id}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>{formatDateTime(e.occurredAt)}</td>
                      <td><div className="cell-main">{e.actorName ?? e.actorId}</div><div className="cell-sub">{e.ipAddress ?? ''}</div></td>
                      <td><ActionLabel action={e.action} details={e.details} /></td>
                      <td><div>{e.targetType ?? '—'}</div><div className="cell-sub mono">{e.targetId ?? ''}</div></td>
                      <td style={{ maxWidth: 360 }} title={e.details ?? ''}><div className="cell-sub" style={{ overflowWrap: 'anywhere', marginTop: 0 }}>{describeAction(e.action, e.details).summary || '—'}</div></td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
            {list.data?.total === 0 && <Empty icon="audit" title={t.audit.emptyTitle} text={t.audit.emptyText} />}
            {list.data && list.data.total > PAGE && (
              <div className="pager">
                <span>{t.identities.shown(skip + 1, Math.min(skip + PAGE, list.data.total), list.data.total)}</span>
                <span style={{ display: 'flex', gap: 8 }}>
                  <button className="btn secondary" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>{t.identities.prev}</button>
                  <button className="btn secondary" disabled={skip + PAGE >= list.data.total} onClick={() => setSkip(skip + PAGE)}>{t.identities.next}</button>
                </span>
              </div>
            )}
          </>
        )}
      </section>
    </>
  )
}

function ActionLabel({ action, details }: { action: string; details: string | null }) {
  const d = describeAction(action, details)
  return (
    <div className="feed-item" data-tone={d.tone} style={{ padding: 0, gridTemplateColumns: '26px minmax(0, 1fr)', gap: 10, alignItems: 'center' }}>
      <span className="feed-icon" style={{ width: 26, height: 26, boxShadow: 'none' }}><Icon name={d.icon} size={13} /></span>
      <div><div className="cell-main">{d.label}</div><div className="cell-sub mono">{action}</div></div>
    </div>
  )
}
