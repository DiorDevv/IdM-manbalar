import { useEffect, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { api, query } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { ChangeRequestItem, ChangeStatus, PagedResult } from '../api/types'
import { describeAction, formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Field, Modal, formError } from '../components/forms'
import { Empty, ErrorBox, PageHead, Pager, Person, SkeletonRows, Tabs, type TabDef } from '../components/ui'

const c = t.changes
const PAGE = 25
type Scope = 'pending' | 'all'
const tone: Record<ChangeStatus, string> = { Pending: 'warn', Applied: 'ok', Rejected: 'bad', Cancelled: 'neutral', Failed: 'bad', Expired: 'neutral' }

/** Audit tafsilotlari (JSON) — kalit: qiymat ro'yxati; massivlar vergul bilan. Faqat matn sifatida chiqadi. */
function detailLines(details: string | null): [string, string][] {
  if (!details) return []
  try {
    const d = JSON.parse(details) as Record<string, unknown>
    return Object.entries(d).filter(([, v]) => v !== null && v !== '' && !(Array.isArray(v) && v.length === 0))
      .map(([k, v]) => [k, Array.isArray(v) ? v.join(', ') : typeof v === 'object' ? JSON.stringify(v) : String(v)])
  } catch { return [] }
}

/**
 * Ikki kishilik tasdiq: xavfli administrator o'zgarishlari (vakolatlar, rol ruxsatlari va qoidalari, SoD, ommaviy o'chirish)
 * boshqa administrator tasdiqlaguncha qo'llanmaydi. Har bir so'rovda aynan nima o'zgarishi (audit yozuvlari) ko'rinadi.
 */
export function ChangesPage() {
  const isAdmin = useIsAdmin()
  const [params, setParams] = useSearchParams()
  const scope: Scope = params.get('tab') === 'all' ? 'all' : 'pending'
  const [skip, setSkip] = useState(0)
  const [deciding, setDeciding] = useState<{ item: ChangeRequestItem; approve: boolean } | null>(null)
  const client = useQueryClient()
  useEffect(() => setSkip(0), [scope])
  const list = useQuery({
    queryKey: ['change-requests', scope, skip],
    queryFn: () => api<PagedResult<ChangeRequestItem>>(`/api/change-requests${query({ status: scope === 'pending' ? 'pending' : undefined, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  })
  const cancel = useMutation({
    mutationFn: (id: string) => api<void>(`/api/change-requests/${id}/cancel`, { method: 'POST' }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: ['change-requests'] }); void client.invalidateQueries({ queryKey: ['me'] }) },
  })
  const tabs: TabDef<Scope>[] = [{ id: 'pending', label: c.tabPending, count: list.data && scope === 'pending' ? list.data.total : undefined, bad: true }, { id: 'all', label: c.tabAll }]

  return (
    <>
      <PageHead title={c.title} subtitle={c.subtitle} />
      {cancel.isError && <div className="card" style={{ marginBottom: 16 }}><ErrorBox error={cancel.error} /></div>}
      <section className="card">
        <Tabs tabs={tabs} current={scope} onSelect={x => setParams({ tab: x }, { replace: true })} />
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : list.isPending ? <table className="data"><SkeletonRows cols={3} /></table> : (
          <>
            {list.data.items.map(x => (
              <div className="list-row" key={x.id} style={{ alignItems: 'flex-start' }}>
                <div className="grow">
                  <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                    <b>{c.kind[x.kind] ?? x.kind}</b><span className={`badge ${tone[x.status]}`}>{c.status[x.status]}</span>
                  </div>
                  {x.preview.map((p, i) => {
                    const d = describeAction(p.action, p.details)
                    return (
                      <div key={i} style={{ marginTop: 6 }}>
                        <div>{d.label}</div>
                        {detailLines(p.details).map(([k, v]) => <div key={k} className="cell-sub"><span className="mono">{k}</span>: {v}</div>)}
                      </div>
                    )
                  })}
                  <div className="cell-sub" style={{ marginTop: 6 }}>
                    {c.requested(x.requestedByName ?? x.requestedBy, formatDateTime(x.requestedAt))}
                    {x.status === 'Pending' && <>, {c.expires(formatDateTime(x.expiresAt))}</>}
                    {x.decidedAt && <>, {c.decided(x.decidedByName ?? x.decidedBy ?? '', formatDateTime(x.decidedAt))}</>}
                  </div>
                  {x.decisionComment && <div className="cell-sub">{c.comment}: {x.decisionComment}</div>}
                  {x.error && <div className="alert bad" style={{ marginTop: 6 }}><Icon name="warning" size={14} /><span>{x.error}</span></div>}
                </div>
                <div style={{ display: 'flex', gap: 6, flexShrink: 0 }}>
                  {isAdmin && x.canDecide && <>
                    <button className="btn primary sm" onClick={() => setDeciding({ item: x, approve: true })}><Icon name="check" size={14} />{c.approve}</button>
                    <button className="btn ghost sm" onClick={() => setDeciding({ item: x, approve: false })}><Icon name="x" size={14} />{c.reject}</button>
                  </>}
                  {x.canCancel && <button className="btn ghost sm" disabled={cancel.isPending} onClick={() => cancel.mutate(x.id)}>{c.cancel}</button>}
                  {x.status === 'Pending' && !x.canDecide && !x.canCancel && <span className="cell-sub">{c.waiting}</span>}
                </div>
              </div>
            ))}
            {list.data.total === 0 && <Empty icon="shield" title={scope === 'pending' ? c.emptyPending : c.emptyAll} text={c.emptyText} />}
            <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />
          </>
        )}
      </section>
      <Decide value={deciding} onClose={() => setDeciding(null)} />
    </>
  )
}

function Decide({ value, onClose }: { value: { item: ChangeRequestItem; approve: boolean } | null; onClose: () => void }) {
  const client = useQueryClient()
  const [comment, setComment] = useState('')
  const save = useMutation({
    mutationFn: () => api<unknown>(`/api/change-requests/${value!.item.id}/${value!.approve ? 'approve' : 'reject'}`,
      { method: 'POST', body: JSON.stringify({ comment: comment.trim() || null }) }),
    // Tasdiq istalgan sahifadagi ma'lumotni o'zgartirishi mumkin (rol, SoD, vakolat): hammasi yangilanadi
    onSuccess: () => { void client.invalidateQueries(); onClose() },
  })
  // Har ochilishda toza forma
  useEffect(() => { if (value) { setComment(''); save.reset() } }, [value])
  if (!value) return null
  const { item, approve } = value
  const tooShort = !approve && comment.trim().length < 5
  return (
    <Modal open title={approve ? c.approveTitle : c.rejectTitle} onClose={onClose} footer={<>
      <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
      <button className={`btn ${approve ? 'primary' : 'danger'}`} disabled={save.isPending || tooShort} onClick={() => save.mutate()}>{approve ? c.approve : c.reject}</button>
    </>}>
      <div className="form">
        {formError(save.error, []) && <div className="alert bad">{formError(save.error, [])}</div>}
        <Person name={item.requestedByName ?? item.requestedBy} sub={c.kind[item.kind] ?? item.kind} />
        <p style={{ margin: 0 }}>{approve ? c.approveText : c.rejectText}</p>
        <Field label={approve ? c.commentOptional : c.reason} help={approve ? undefined : c.reasonHelp}>
          <textarea className="input" maxLength={1000} value={comment} onChange={e => setComment(e.target.value)} />
        </Field>
      </div>
    </Modal>
  )
}
