import { useDeferredValue, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, query } from '../api/client'
import type { DelegationItem, MyDelegations, PersonBrief } from '../api/types'
import { formatDate, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Field, Modal, formError } from '../components/forms'
import { ConfirmDialog, Empty, ErrorBox } from '../components/ui'

const d = t.delegations
const day = (offset: number) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10)
// Tugash vaqti — keyingi kun 00:00: oxirgi kun ko'rsatiladi
const lastDay = (validTo: string) => formatDate(new Date(Date.parse(validTo) - 60_000).toISOString())

/** O'rinbosarlik: men tayinlaganlar va men o'rinbosar bo'lganlar; yangi o'rinbosar tayinlash. */
export function DelegationsPanel() {
  const client = useQueryClient()
  const [adding, setAdding] = useState(false)
  const [revoking, setRevoking] = useState<DelegationItem | null>(null)
  const list = useQuery({ queryKey: ['delegations'], queryFn: () => api<MyDelegations>('/api/me/delegations') })
  const revoke = useMutation({
    mutationFn: (id: string) => api<void>(`/api/delegations/${id}/revoke`, { method: 'POST' }),
    onSuccess: () => client.invalidateQueries(),
  })
  const row = (x: DelegationItem, given: boolean) => (
    <div className="list-row" key={x.id}>
      <span className="chip-icon"><Icon name="people" size={14} /></span>
      <div className="grow">
        <div className="cell-main">{given ? x.delegateName : d.actingFor(x.delegatorName)}</div>
        <div className="cell-sub">{d.period(formatDate(x.validFrom), lastDay(x.validTo))}{x.reason ? ` · ${x.reason}` : ''}</div>
      </div>
      <span className={`badge ${x.active ? 'ok' : 'neutral'}`}>{x.active ? d.active : d.upcoming}</span>
      {given && <button className="btn ghost small" onClick={() => setRevoking(x)}>{d.revoke}</button>}
    </div>
  )
  return (
    <section className="card" style={{ marginTop: 16 }}>
      <div className="card-head"><h2>{d.title}</h2>
        <button className="btn secondary" onClick={() => setAdding(true)}><Icon name="people" size={15} />{d.add}</button></div>
      <p className="cell-sub" style={{ padding: '0 16px' }}>{d.subtitle}</p>
      {list.isError && <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} />}
      {revoke.isError && <ErrorBox error={revoke.error} />}
      {list.data && list.data.given.length + list.data.received.length === 0 && <Empty icon="people" title={d.none} text={d.noneText} />}
      {list.data && list.data.given.length > 0 && <><div className="cell-sub" style={{ padding: '8px 16px 0', fontWeight: 600 }}>{d.given}</div>{list.data.given.map(x => row(x, true))}</>}
      {list.data && list.data.received.length > 0 && <><div className="cell-sub" style={{ padding: '8px 16px 0', fontWeight: 600 }}>{d.received}</div>{list.data.received.map(x => row(x, false))}</>}
      {adding && <AddDelegation onClose={() => setAdding(false)} />}
      <ConfirmDialog open={!!revoking} title={d.revokeTitle} text={d.revokeText} confirmLabel={d.revoke} danger
        onCancel={() => setRevoking(null)} onConfirm={() => { if (revoking) revoke.mutate(revoking.id); setRevoking(null) }} />
    </section>
  )
}

function AddDelegation({ onClose }: { onClose: () => void }) {
  const client = useQueryClient()
  const [search, setSearch] = useState('')
  const deferred = useDeferredValue(search.trim())
  const [who, setWho] = useState<PersonBrief | null>(null)
  const [from, setFrom] = useState(day(0))
  const [until, setUntil] = useState(day(14))
  const [reason, setReason] = useState('')
  const people = useQuery({
    queryKey: ['directory', deferred],
    queryFn: () => api<PersonBrief[]>(`/api/directory${query({ search: deferred })}`),
    enabled: deferred.length >= 2,
  })
  const save = useMutation({
    mutationFn: () => api<string>('/api/me/delegations', { method: 'POST', body: JSON.stringify({
      delegatorId: null, delegateId: who?.id, from, until, reason: reason.trim() || null,
    }) }),
    onSuccess: () => { void client.invalidateQueries(); onClose() },
  })
  const valid = !!who && from >= day(0) && until >= from && until <= day(180)
  return (
    <Modal open title={d.add} onClose={onClose} footer={<>
      <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
      <button className="btn primary" disabled={!valid || save.isPending} onClick={() => save.mutate()}>{d.add}</button>
    </>}>
      <div className="form">
        {formError(save.error, []) && <div className="alert bad">{formError(save.error, [])}</div>}
        <div className="alert info">{d.rules}</div>
        <Field label={d.who}>
          <div className="picker">
            {who && <div className="picker-item"><span className="name"><Icon name="user" size={13} /> {who.name}</span><span className="app-code">{who.employeeNumber}</span></div>}
            <label className="search"><Icon name="search" size={15} />
              <input className="input" placeholder={d.search} value={search} onChange={e => setSearch(e.target.value)} aria-label={d.search} />
            </label>
            {deferred.length >= 2 && (
              <div className="picker-list" role="listbox">
                {people.data?.length === 0 && <div className="muted" style={{ padding: 10, fontSize: 13 }}>{t.cmd.nothing}</div>}
                {people.data?.map(p => (
                  <button type="button" key={p.id} className="picker-item" role="option" aria-selected={who?.id === p.id}
                    onClick={() => { setWho(p); setSearch('') }} style={{ width: '100%', textAlign: 'left', background: 'none', border: 0, cursor: 'pointer' }}>
                    <span className="name">{p.name}</span><span className="app-code">{p.employeeNumber}{p.department ? ` · ${p.department}` : ''}</span>
                  </button>
                ))}
              </div>
            )}
          </div>
        </Field>
        <div className="form-row">
          <Field label={d.from}><input className="input" type="date" min={day(0)} max={day(180)} value={from} onChange={e => setFrom(e.target.value)} /></Field>
          <Field label={d.until} help={d.untilHelp}><input className="input" type="date" min={from} max={day(180)} value={until} onChange={e => setUntil(e.target.value)} /></Field>
        </div>
        <Field label={d.reason}>
          <input className="input" value={reason} maxLength={500} placeholder={d.reasonPlaceholder} onChange={e => setReason(e.target.value)} />
        </Field>
      </div>
    </Modal>
  )
}
