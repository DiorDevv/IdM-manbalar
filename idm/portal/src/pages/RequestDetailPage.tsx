import { useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { ApprovalItem, RequestDetails } from '../api/types'
import { formatDate, formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { ConfirmDialog, Empty, ErrorBox, PageHead, Person, RiskBadge } from '../components/ui'
import { Conflict } from './SodPage'
import { RequestStatusBadge } from './RequestsPage'

const q = t.requests

function Fact({ icon, label, children }: { icon: string; label: string; children: ReactNode }) {
  return <div className="fact"><div className="k"><Icon name={icon} size={14} />{label}</div><div className="v">{children}</div></div>
}

export function RequestDetailPage() {
  const { id = '' } = useParams()
  const client = useQueryClient()
  const [comment, setComment] = useState('')
  const [confirmCancel, setConfirmCancel] = useState(false)
  const key = ['request', id]
  const request = useQuery({ queryKey: key, queryFn: () => api<RequestDetails>(`/api/requests/${id}`), enabled: !!id })
  const done = (r: RequestDetails) => {
    client.setQueryData(key, r)
    setComment('')
    void client.invalidateQueries({ predicate: x => x.queryKey[0] !== 'request' })   // ro'yxatlar, navbat soni
  }
  const decide = useMutation({
    mutationFn: (approve: boolean) => api<RequestDetails>(`/api/requests/${id}/decision`, { method: 'POST', body: JSON.stringify({ approve, comment: comment.trim() || null }) }),
    onSuccess: done,
  })
  const cancel = useMutation({ mutationFn: () => api<RequestDetails>(`/api/requests/${id}/cancel`, { method: 'POST' }), onSuccess: done })

  const crumbs = <><Link to="/requests">{q.title}</Link><Icon name="chevron" size={13} /></>
  if (request.isError) {
    return <div className="card">{request.error instanceof ApiError && request.error.status === 404 ? <Empty icon="request" title={q.notFound} /> : <ErrorBox error={request.error} />}</div>
  }
  if (!request.data) return <div className="crumbs">{crumbs}{t.loading}</div>
  const r = request.data
  const currentStep = r.status === 'Pending' ? r.approvals.find(a => a.decision === 'Pending') : undefined

  return (
    <>
      <PageHead crumbs={<>{crumbs}{r.roleName}</>} title={r.roleName}
        subtitle={<span style={{ display: 'inline-flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          <RequestStatusBadge status={r.status} /><span className="mono">{r.roleCode}</span><RiskBadge risk={r.risk} />
          {r.sodConflicts.length > 0 && <span className="badge bad plain"><Icon name="shield" size={12} />SoD</span>}
        </span>}
        actions={r.canCancel && <button className="btn ghost" onClick={() => setConfirmCancel(true)}><Icon name="x" size={16} />{q.cancel}</button>} />

      {r.sodConflicts.length > 0 && (
        <div className="alert bad" style={{ marginBottom: 16 }}>
          <Icon name="shield" size={18} />
          <div className="grow"><strong>{q.sodWarning}</strong>
            {r.sodConflicts.map(c => <div key={c.ruleCode} style={{ marginTop: 6 }}><div style={{ marginBottom: 4 }}>{c.ruleName} ({c.ruleCode})</div><Conflict left={c.left} right={c.right} /></div>)}
          </div>
        </div>
      )}

      <div className="request-layout">
        <div className="grid">
          <section className="card">
            <div className="facts" style={{ borderTop: 0 }}>
              <Fact icon="user" label={q.colFor}><Person name={r.beneficiary.name} sub={<span className="mono">{r.beneficiary.employeeNumber}{r.beneficiary.department ? ` · ${r.beneficiary.department}` : ''}</span>} to={`/identities/${r.beneficiary.id}`} /></Fact>
              <Fact icon="request" label={q.requester}>{r.requester.name}</Fact>
              <Fact icon="calendar" label={q.created}>{formatDateTime(r.createdAt)}</Fact>
              <Fact icon="clock" label={q.validUntil}>{r.validTo ? formatDate(r.validTo) : q.forever}</Fact>
              {r.closedAt && <Fact icon="check" label={q.closed}>{formatDateTime(r.closedAt)}</Fact>}
            </div>
            <div className="card-body" style={{ borderTop: '1px solid var(--border)' }}>
              <div className="field"><span className="label">{q.justification}</span><blockquote className="quote">{r.justification}</blockquote></div>
            </div>
            <div className="card-body" style={{ borderTop: '1px solid var(--border)' }}>
              <div className="field"><span className="label">{q.entitlements}</span>
                <div className="chips">{r.entitlements.length ? r.entitlements.map(e => <span key={e} className="chip">{e}</span>) : <span className="muted">—</span>}</div>
              </div>
            </div>
          </section>

          {r.canDecide && (
            <section className="card decision-card">
              <div className="card-head"><h2>{q.yourTurn}</h2>{currentStep && <span className="badge info plain">{q.step[currentStep.kind]}</span>}</div>
              <div className="card-body form">
                {decide.isError && <div className="alert bad"><Icon name="warning" size={16} /><span>{(decide.error as Error).message}</span></div>}
                <div className="field"><label>{q.comment}<textarea className="input" rows={3} maxLength={2000} value={comment} onChange={e => setComment(e.target.value)} /></label>
                  <span className="help">{q.commentHelp}</span></div>
                <div style={{ display: 'flex', gap: 8, justifyContent: 'flex-end' }}>
                  <button className="btn secondary" disabled={decide.isPending || comment.trim().length < 3} onClick={() => decide.mutate(false)}><Icon name="x" size={16} />{q.reject}</button>
                  <button className="btn primary" disabled={decide.isPending} onClick={() => decide.mutate(true)}><Icon name="check" size={16} />{q.approve}</button>
                </div>
              </div>
            </section>
          )}
        </div>

        <section className="card sticky">
          <div className="card-head"><h2>{q.chain}</h2></div>
          <ol className="chain">{r.approvals.map(a => <ChainStep key={a.step} a={a} current={a === currentStep} />)}</ol>
          {cancel.isError && <ErrorBox error={cancel.error} />}
        </section>
      </div>

      <ConfirmDialog open={confirmCancel} title={q.cancel} text={q.cancelConfirm} confirmLabel={q.cancel} danger
        onCancel={() => setConfirmCancel(false)} onConfirm={() => { setConfirmCancel(false); cancel.mutate() }} />
    </>
  )
}

function ChainStep({ a, current }: { a: ApprovalItem; current: boolean }) {
  const state = a.decision === 'Approved' ? 'done' : a.decision === 'Rejected' ? 'rejected' : current ? 'current' : ''
  const auto = a.decidedBy === 'system'
  return (
    <li className={state}>
      <span className="node">{a.decision === 'Approved' ? <Icon name="check" size={14} strokeWidth={2.5} /> : a.decision === 'Rejected' ? <Icon name="x" size={14} strokeWidth={2.5} /> : a.step}</span>
      <div>
        <div className="who">{q.step[a.kind]}</div>
        <div className="meta">{a.approverName ?? q.adminPool}
          {a.decidedAt ? ` · ${auto ? q.auto : a.decidedByName ?? a.decidedBy} · ${formatDateTime(a.decidedAt)}` : current ? ` · ${q.waiting}` : ''}</div>
        {a.comment && <div className="note">{a.comment}</div>}
      </div>
    </li>
  )
}
