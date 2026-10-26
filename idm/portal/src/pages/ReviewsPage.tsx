import { useDeferredValue, useMemo, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { api } from '../api/client'
import { useIsAdmin, useMe } from '../api/me'
import type { ApplicationItem, CampaignBody, CampaignItem, CampaignPreview, ReviewItem } from '../api/types'
import { formatDate, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Field, Modal, formError } from '../components/forms'
import { Empty, ErrorBox, PageHead, Person, SkeletonRows, Tabs, type TabDef } from '../components/ui'

const v = t.reviews
type Tab = 'inbox' | 'campaigns'

export function ReviewsPage() {
  const me = useMe()
  const [params, setParams] = useSearchParams()
  const reader = !!me.data?.roles.some(r => r === 'idm.admin' || r === 'idm.auditor')
  const tabs: TabDef<Tab>[] = [
    { id: 'inbox', label: v.tabInbox, count: me.data?.reviewCount, bad: true },
    ...(reader ? [{ id: 'campaigns' as const, label: v.tabCampaigns }] : []),
  ]
  const tab: Tab = tabs.some(x => x.id === params.get('tab')) ? params.get('tab') as Tab : 'inbox'
  return (
    <>
      <PageHead title={v.title} subtitle={v.subtitle} />
      <section className="card">
        {tabs.length > 1 && <Tabs tabs={tabs} current={tab} onSelect={x => setParams({ tab: x }, { replace: true })} />}
        {tab === 'inbox' ? <Inbox /> : <Campaigns />}
      </section>
    </>
  )
}

/** Overdue: muddat (sana) bugundan oldin. */
export function isOverdue(due: string): boolean {
  return due < new Date().toISOString().slice(0, 10)
}

function Inbox() {
  const client = useQueryClient()
  const items = useQuery({ queryKey: ['reviews', 'inbox'], queryFn: () => api<ReviewItem[]>('/api/reviews/inbox') })
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirm, setConfirm] = useState<{ keep: boolean; ids: string[] } | null>(null)
  const [comment, setComment] = useState('')
  const decide = useMutation({
    mutationFn: (x: { keep: boolean; ids: string[]; comment: string }) =>
      api<number>('/api/reviews/decide', { method: 'POST', body: JSON.stringify({ ids: x.ids, keep: x.keep, comment: x.comment || null }) }),
    onSuccess: () => { setConfirm(null); setComment(''); setSelected(new Set()) },
    onSettled: () => client.invalidateQueries({ predicate: q => q.queryKey[0] === 'reviews' || q.queryKey[0] === 'me' }),
  })

  // Kampaniya → xodim → yozuvlar
  const groups = useMemo(() => {
    const byCampaign = new Map<string, { name: string; due: string; people: Map<string, ReviewItem[]> }>()
    for (const i of items.data ?? []) {
      const c = byCampaign.get(i.campaignId) ?? { name: i.campaignName, due: i.dueDate, people: new Map() }
      c.people.set(i.identityId, [...(c.people.get(i.identityId) ?? []), i])
      byCampaign.set(i.campaignId, c)
    }
    return [...byCampaign.entries()]
  }, [items.data])

  if (items.isError) return <ErrorBox error={items.error} onRetry={() => { void items.refetch() }} />
  if (items.isPending) return <table className="data"><SkeletonRows cols={4} /></table>
  if (items.data.length === 0) return <Empty icon="review" title={v.emptyInbox} text={v.emptyInboxText} />
  const toggle = (id: string) => setSelected(s => { const next = new Set(s); if (!next.delete(id)) next.add(id); return next })
  const chosen = [...selected]

  return (
    <>
      <div className="toolbar">
        <span className="muted">{chosen.length > 0 ? v.selected(chosen.length) : v.items(items.data.length)}</span>
        {chosen.length > 0 && (
          <span style={{ marginLeft: 'auto', display: 'inline-flex', gap: 8 }}>
            <button className="btn secondary sm" onClick={() => setConfirm({ keep: true, ids: chosen })}><Icon name="check" size={15} />{v.keep}</button>
            <button className="btn danger sm" onClick={() => setConfirm({ keep: false, ids: chosen })}><Icon name="x" size={15} />{v.revoke}</button>
          </span>
        )}
      </div>
      {decide.isError && !confirm && <ErrorBox error={decide.error} />}
      {groups.map(([campaignId, c]) => (
        <div key={campaignId}>
          <div className="card-head">
            <h2>{c.name}</h2>
            <span className={`badge ${isOverdue(c.due) ? 'bad' : 'neutral'}`}>{v.due}: {formatDate(c.due)}{isOverdue(c.due) && ` · ${v.overdue}`}</span>
          </div>
          {[...c.people.values()].map(list => {
            const p = list[0]!
            return (
              <div key={p.identityId} className="review-group">
                <div className="review-group-head">
                  <div className="grow">
                    <Person name={p.identityName} sub={<><span className="mono">{p.employeeNumber}</span>{p.department && <> · {p.department}</>}{p.jobTitle && <> · {p.jobTitle}</>}</>} />
                  </div>
                  <button className="btn ghost sm" onClick={() => setConfirm({ keep: true, ids: list.map(i => i.id) })}><Icon name="check" size={15} />{v.keepAll}</button>
                </div>
                <div className="table-wrap">
                  <table className="data">
                    <tbody>
                      {list.map(i => (
                        <tr key={i.id} className={selected.has(i.id) ? 'selected' : undefined}>
                          <td style={{ width: 36 }}><input type="checkbox" aria-label={`${i.title}`} checked={selected.has(i.id)} onChange={() => toggle(i.id)} /></td>
                          <td>
                            <div className="cell-main" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                              {i.isPrivileged && <Icon name="lock" size={14} />}{i.title}
                            </div>
                            <div className="cell-sub"><span className="mono">{i.subtitle}</span> · {i.applicationCode}</div>
                          </td>
                          <td title={v.kindHelp[i.kind]}>
                            <span className={`badge plain ${i.kind === 'Entitlement' ? 'warn' : 'info'}`}>{v.kind[i.kind]}</span>
                          </td>
                          <td style={{ textAlign: 'right' }}>
                            <span className="decision-actions">
                              <button className="btn secondary sm" onClick={() => setConfirm({ keep: true, ids: [i.id] })}>{v.keep}</button>
                              <button className="btn ghost sm" style={{ color: 'var(--bad)' }} onClick={() => setConfirm({ keep: false, ids: [i.id] })}>{v.revoke}</button>
                            </span>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )
          })}
        </div>
      ))}

      <Modal open={!!confirm} title={confirm?.keep ? v.keepTitle : v.revokeTitle} onClose={() => { setConfirm(null); decide.reset() }}
        footer={<>
          <button className="btn secondary" onClick={() => { setConfirm(null); decide.reset() }}>{t.cancel}</button>
          <button className={`btn ${confirm?.keep ? 'primary' : 'danger'}`} disabled={decide.isPending}
            onClick={() => confirm && decide.mutate({ ...confirm, comment })}>{confirm?.keep ? v.keep : v.revoke}</button>
        </>}>
        <p style={{ marginTop: 0 }}>{confirm && (confirm.keep ? v.keepText(confirm.ids.length) : v.revokeText(confirm.ids.length))}</p>
        <Field label={v.comment} help={v.commentHelp}>
          <textarea className="input" maxLength={2000} value={comment} onChange={e => setComment(e.target.value)} />
        </Field>
        {decide.isError && <div className="alert bad"><Icon name="warning" size={16} /><span>{formError(decide.error, [])}</span></div>}
      </Modal>
    </>
  )
}

function Campaigns() {
  const isAdmin = useIsAdmin()
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)
  const list = useQuery({ queryKey: ['reviews', 'campaigns'], queryFn: () => api<CampaignItem[]>('/api/reviews/campaigns') })
  return (
    <>
      {isAdmin && (
        <div className="toolbar">
          <button className="btn primary sm" style={{ marginLeft: 'auto' }} onClick={() => setCreating(true)}><Icon name="sparkle" size={15} />{v.create}</button>
        </div>
      )}
      {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
        <div className="table-wrap">
          <table className="data">
            <thead><tr><th>{v.colName}</th><th>{v.colScope}</th><th>{v.due}</th><th>{v.colProgress}</th><th className="num">{v.colRevoked}</th><th>{v.colStatus}</th></tr></thead>
            {list.isPending ? <SkeletonRows cols={6} /> : (
              <tbody>
                {list.data.map(c => {
                  const done = c.total - c.pending
                  return (
                    <tr key={c.id} className="clickable" onClick={() => navigate(`/reviews/campaigns/${c.id}`)}>
                      <td>
                        <Link to={`/reviews/campaigns/${c.id}`} className="cell-main" onClick={e => e.stopPropagation()}>{c.name}</Link>
                        <div className="cell-sub">{c.createdByName ?? c.createdBy} · {formatDate(c.createdAt)}</div>
                      </td>
                      <td>{[c.department ?? v.scopeAll, c.applicationCode, c.privilegedOnly ? v.privilegedOnly : null].filter(Boolean).join(' · ')}</td>
                      <td style={{ whiteSpace: 'nowrap' }}>
                        {formatDate(c.dueDate)}
                        {c.status === 'Active' && isOverdue(c.dueDate) && <div className="cell-sub" style={{ color: 'var(--bad)' }}>{v.overdue}</div>}
                      </td>
                      <td style={{ minWidth: 140 }}>
                        <div className={`progress${c.pending === 0 ? ' done' : ''}`}><span style={{ width: `${c.total ? (done * 100) / c.total : 0}%` }} /></div>
                        <div className="cell-sub num">{v.progress(done, c.total)}</div>
                      </td>
                      <td className="num">{c.revoked}{c.revokedOpen > 0 && <div className="cell-sub">{c.revokedOpen} {v.remediationPending}</div>}</td>
                      <td><span className={`badge ${c.status === 'Active' ? 'info' : c.status === 'Completed' ? 'ok' : 'neutral'}`}>{v.status[c.status]}</span></td>
                    </tr>
                  )
                })}
              </tbody>
            )}
          </table>
          {list.data?.length === 0 && <Empty icon="review" title={v.emptyCampaigns} text={v.emptyCampaignsText} />}
        </div>
      )}
      {creating && <NewCampaign onClose={() => setCreating(false)} onCreated={id => navigate(`/reviews/campaigns/${id}`)} />}
    </>
  )
}

function NewCampaign({ onClose, onCreated }: { onClose: () => void; onCreated: (id: string) => void }) {
  const client = useQueryClient()
  const inTwoWeeks = new Date(Date.now() + 14 * 864e5).toISOString().slice(0, 10)
  const [form, setForm] = useState<CampaignBody>({ name: '', description: null, department: null, applicationId: null, privilegedOnly: false, dueDate: inTwoWeeks })
  const set = <K extends keyof CampaignBody>(k: K, value: CampaignBody[K]) => setForm(f => ({ ...f, [k]: value }))
  const apps = useQuery({ queryKey: ['applications'], queryFn: () => api<ApplicationItem[]>('/api/applications') })
  // Jonli oldindan ko'rish: qamrov o'zgarganda (nomsiz ham) — nechta ruxsat, xodim va rahbar
  const scope = useDeferredValue(JSON.stringify({ ...form, name: form.name.trim().length >= 3 ? form.name : 'preview' }))
  const preview = useQuery({
    queryKey: ['reviews', 'preview', scope],
    queryFn: () => api<CampaignPreview>('/api/reviews/campaigns/preview', { method: 'POST', body: scope }),
    retry: false,
  })
  const create = useMutation({
    mutationFn: () => api<CampaignItem>('/api/reviews/campaigns', { method: 'POST', body: JSON.stringify(form) }),
    onSuccess: c => { void client.invalidateQueries({ queryKey: ['reviews'] }); onCreated(c.id) },
  })
  const p = preview.data
  return (
    <Modal open title={v.newTitle} onClose={onClose} wide
      footer={<>
        <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
        <button className="btn primary" disabled={create.isPending || form.name.trim().length < 3 || !p?.items} onClick={() => create.mutate()}>
          {create.isPending ? v.launching : v.launch}
        </button>
      </>}>
      <div className="form">
        <Field label={v.name}><input className="input" maxLength={256} placeholder={v.namePlaceholder} value={form.name} onChange={e => set('name', e.target.value)} /></Field>
        <Field label={v.description}><textarea className="input" maxLength={2000} value={form.description ?? ''} onChange={e => set('description', e.target.value || null)} /></Field>
        <div className="form-row">
          <Field label={v.department} help={v.departmentHelp}>
            <input className="input" maxLength={256} value={form.department ?? ''} onChange={e => set('department', e.target.value || null)} />
          </Field>
          <Field label={v.application}>
            <select className="select" value={form.applicationId ?? ''} onChange={e => set('applicationId', e.target.value || null)}>
              <option value="">{v.allApplications}</option>
              {apps.data?.filter(a => a.kind !== 'HumanResources').map(a => <option key={a.id} value={a.id}>{a.name} ({a.code})</option>)}
            </select>
          </Field>
          <Field label={v.dueDate}><input className="input" type="date" value={form.dueDate} onChange={e => set('dueDate', e.target.value)} /></Field>
        </div>
        <label className="switch"><input type="checkbox" checked={form.privilegedOnly} onChange={e => set('privilegedOnly', e.target.checked)} />{v.privileged}</label>
        <div className={`alert ${preview.isError || p?.items === 0 ? 'warn' : 'info'}`}>
          <Icon name={preview.isError || p?.items === 0 ? 'warning' : 'review'} size={16} />
          <span>{preview.isError ? formError(preview.error, []) : !p ? t.loading : p.items === 0 ? v.previewEmpty : v.preview(p)}</span>
        </div>
        {create.isError && <div className="alert bad"><Icon name="warning" size={16} /><span>{formError(create.error, [])}</span></div>}
      </div>
    </Modal>
  )
}
