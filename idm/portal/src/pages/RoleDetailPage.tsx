import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { AttributeValues, BirthrightResult, EntitlementPick, RoleDetails } from '../api/types'
import { formatDate, t } from '../i18n'
import { Icon } from '../components/Icon'
import { EntitlementPicker, Field, Modal, fieldError, formError } from '../components/forms'
import { ConfirmDialog, Empty, ErrorBox, PageHead, Person, RiskBadge, StatusBadge, Stat, Tabs } from '../components/ui'
import { RoleForm } from './RoleForm'

const r = t.roles
type Tab = 'entitlements' | 'rules' | 'members'

export function RoleDetailPage() {
  const { id = '' } = useParams()
  const navigate = useNavigate()
  const isAdmin = useIsAdmin()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const tab = (['entitlements', 'rules', 'members'] as const).find(x => x === params.get('tab')) ?? 'entitlements'
  const [editing, setEditing] = useState(false)
  const [adding, setAdding] = useState(false)
  const [ruleOpen, setRuleOpen] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState(false)
  const key = ['role', id]
  const role = useQuery({ queryKey: key, queryFn: () => api<RoleDetails>(`/api/roles/${id}`), enabled: !!id })
  const update = (data: RoleDetails) => { client.setQueryData(key, data); void client.invalidateQueries({ queryKey: ['roles'] }) }

  const setEntitlements = useMutation({
    mutationFn: (ids: string[]) => api<RoleDetails>(`/api/roles/${id}/entitlements`, { method: 'PUT', body: JSON.stringify({ entitlementIds: ids }) }),
    onSuccess: update,
  })
  const removeRule = useMutation({
    mutationFn: (ruleId: string) => api<RoleDetails>(`/api/roles/${id}/rules/${ruleId}`, { method: 'DELETE' }),
    onSuccess: update,
  })
  const recalc = useMutation({
    mutationFn: () => api<BirthrightResult>('/api/roles/recalculate', { method: 'POST' }),
    onSuccess: () => client.invalidateQueries(),
  })
  const remove = useMutation({
    mutationFn: () => api<void>(`/api/roles/${id}`, { method: 'DELETE' }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: ['roles'] }); navigate('/roles') },
  })

  const crumbs = <><Link to="/roles">{r.title}</Link><Icon name="chevron" size={13} /></>
  if (role.isError) return <div className="card">{role.error instanceof ApiError && role.error.status === 404 ? <Empty icon="role" title={t.cmd.nothing} /> : <ErrorBox error={role.error} />}</div>
  if (!role.data) return <div className="crumbs">{crumbs}{t.loading}</div>
  const d = role.data

  return (
    <>
      <PageHead crumbs={<>{crumbs}{d.name}</>} title={d.name}
        subtitle={<span style={{ display: 'inline-flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          <span className="mono">{d.code}</span> · <RiskBadge risk={d.risk} />{d.isRequestable && <> · {r.requestable}</>}{d.description && <> · {d.description}</>}
        </span>}
        actions={isAdmin && <>
          <button className="btn ghost" onClick={() => setConfirmDelete(true)}><Icon name="x" size={16} />{r.delete}</button>
          <button className="btn secondary" onClick={() => setEditing(true)}>{r.edit}</button>
        </>} />

      {d.sodConflicts.length > 0 && <div className="alert bad" style={{ marginBottom: 16 }}><Icon name="warning" size={18} /><span>{r.sodConflict(d.sodConflicts.join(', '))}</span></div>}
      {remove.isError && <div className="alert bad" style={{ marginBottom: 16 }}><Icon name="warning" size={18} /><span>{(remove.error as Error).message}</span></div>}
      {(setEntitlements.isError || removeRule.isError) && <div className="card" style={{ marginBottom: 16 }}><ErrorBox error={setEntitlements.error ?? removeRule.error} /></div>}

      <div className="grid stats">
        <Stat icon="people" tone="violet" label={r.colEntitlements} value={d.entitlements.length} hint={`${d.entitlements.filter(e => e.isPrivileged).length} ${t.applications.privileged}`} />
        <Stat icon="sparkle" tone="teal" label={r.colRules} value={d.rules.length} hint={r.matches(d.rules.reduce((s, x) => s + x.matchCount, 0))} />
        <Stat icon="user" label={r.colMembers} value={d.members.length} hint={`${d.members.filter(m => m.source === 'Birthright').length} ${r.source.Birthright!.toLowerCase()}`} />
      </div>

      <section className="card">
        <Tabs current={tab} onSelect={(x: Tab) => setParams({ tab: x }, { replace: true })} tabs={[
          { id: 'entitlements', label: r.tabEntitlements, count: d.entitlements.length },
          { id: 'rules', label: r.tabRules, count: d.rules.length },
          { id: 'members', label: r.tabMembers, count: d.members.length },
        ]} />

        {tab === 'entitlements' && <>
          {isAdmin && <div className="toolbar"><div className="spacer" style={{ flex: 1 }} /><button className="btn secondary sm" onClick={() => setAdding(true)}><Icon name="sparkle" size={14} />{r.addEntitlements}</button></div>}
          {d.entitlements.length === 0 ? <Empty icon="lock" title={r.noEntitlements} text={r.noEntitlementsText} /> : d.entitlements.map(e => (
            <div className="list-row" key={e.id}>
              <span className="chip-icon" style={e.isPrivileged ? { ['--tone' as string]: 'var(--warn)', ['--tone-soft' as string]: 'var(--warn-soft)' } : undefined}>
                <Icon name={e.isPrivileged ? 'lock' : 'people'} size={14} />
              </span>
              <div className="grow"><div className="cell-main">{e.name}</div><div className="cell-sub">{e.applicationCode}{e.isPrivileged ? ` · ${t.applications.privileged}` : ''}</div></div>
              <RiskBadge risk={e.risk} />
              {isAdmin && <button className="icon-btn" aria-label={r.remove} title={r.remove} disabled={setEntitlements.isPending}
                onClick={() => setEntitlements.mutate(d.entitlements.filter(x => x.id !== e.id).map(x => x.id))}><Icon name="x" size={15} /></button>}
            </div>
          ))}
        </>}

        {tab === 'rules' && <>
          {isAdmin && <div className="toolbar">
            <span className="muted" style={{ fontSize: 12.5, flex: 1 }}>{r.applyHelp}</span>
            <button className="btn secondary sm" disabled={recalc.isPending} onClick={() => recalc.mutate()}><Icon name="sync" size={14} />{r.recalc}</button>
            <button className="btn secondary sm" onClick={() => setRuleOpen(true)}><Icon name="sparkle" size={14} />{r.addRule}</button>
          </div>}
          {recalc.data && <div className="alert ok" style={{ margin: 12 }}><Icon name="check" size={16} /><span>{r.recalcDone(recalc.data)}</span></div>}
          {recalc.isError && <ErrorBox error={recalc.error} />}
          {d.rules.length === 0 ? <Empty icon="sparkle" title={r.noRules} text={r.noRulesText} /> : d.rules.map(x => (
            <div className="list-row" key={x.id}>
              <span className="chip-icon" style={{ ['--tone' as string]: 'var(--teal)', ['--tone-soft' as string]: 'var(--teal-soft)' }}><Icon name="sparkle" size={14} /></span>
              <div className="grow" style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
                <span className="rule-chip">{r.department}: <b>{x.department ?? r.any}</b></span>
                <span className="muted">+</span>
                <span className="rule-chip">{r.jobTitle}: <b>{x.jobTitle ?? r.any}</b></span>
                <span className="muted" style={{ fontSize: 12.5, marginLeft: 6 }}>{r.matches(x.matchCount)} · {x.createdBy}, {formatDate(x.createdAt)}</span>
              </div>
              {isAdmin && <button className="icon-btn" aria-label={r.remove} title={r.remove} disabled={removeRule.isPending} onClick={() => removeRule.mutate(x.id)}><Icon name="x" size={15} /></button>}
            </div>
          ))}
        </>}

        {tab === 'members' && (d.members.length === 0 ? <Empty icon="people" title={r.noMembers} /> : (
          <table className="data">
            <thead><tr><th>{t.identities.colName}</th><th>{t.identities.colDepartment}</th><th>{t.identities.colStatus}</th><th>{t.identities.colSource}</th><th>{t.identities.fields.created}</th></tr></thead>
            <tbody>
              {d.members.map(m => (
                <tr key={`${m.identityId}-${m.source}`}>
                  <td><Person name={m.name} sub={<span className="mono">{m.employeeNumber}</span>} to={`/identities/${m.identityId}`} /></td>
                  <td>{m.department ?? '—'}</td>
                  <td><StatusBadge status={m.status} /></td>
                  <td><span className={`badge ${m.source === 'Birthright' ? 'info' : 'neutral'} plain`}>{r.source[m.source]}</span></td>
                  <td>{formatDate(m.validFrom)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        ))}
      </section>

      <RoleForm open={editing} role={d} onClose={() => setEditing(false)} onSaved={x => { setEditing(false); update(x) }} />
      <AddEntitlements open={adding} current={d.entitlements} pending={setEntitlements.isPending} onClose={() => setAdding(false)}
        onSave={next => setEntitlements.mutate(next.map(x => x.id), { onSuccess: () => setAdding(false) })} />
      <AddRule open={ruleOpen} roleId={id} onClose={() => setRuleOpen(false)} onSaved={x => { setRuleOpen(false); update(x) }} />
      <ConfirmDialog open={confirmDelete} title={r.delete} text={r.deleteConfirm} confirmLabel={r.delete} danger
        onCancel={() => setConfirmDelete(false)} onConfirm={() => { setConfirmDelete(false); remove.mutate() }} />
    </>
  )
}

function AddEntitlements({ open, current, pending, onClose, onSave }: { open: boolean; current: EntitlementPick[]; pending: boolean; onClose: () => void; onSave: (next: EntitlementPick[]) => void }) {
  const [selected, setSelected] = useState<EntitlementPick[]>([])
  useEffect(() => { if (open) setSelected([]) }, [open])
  return (
    <Modal open={open} title={r.addEntitlements} onClose={() => { setSelected([]); onClose() }} wide footer={<>
      <span className="muted" style={{ marginRight: 'auto', fontSize: 12.5 }}>{r.selected(selected.length)}</span>
      <button className="btn secondary" onClick={() => { setSelected([]); onClose() }}>{t.cancel}</button>
      <button className="btn primary" disabled={selected.length === 0 || pending} onClick={() => { onSave([...current, ...selected]); setSelected([]) }}>{r.save}</button>
    </>}>
      <EntitlementPicker selected={selected} onChange={setSelected} exclude={current.map(c => c.id)} />
    </Modal>
  )
}

function AddRule({ open, roleId, onClose, onSaved }: { open: boolean; roleId: string; onClose: () => void; onSaved: (role: RoleDetails) => void }) {
  const [department, setDepartment] = useState('')
  const [jobTitle, setJobTitle] = useState('')
  // Har ochilishda toza forma (oldin Esc bilan yopilgan bo'lsa ham)
  useEffect(() => { if (open) { setDepartment(''); setJobTitle(''); save.reset() } }, [open])
  const values = useQuery({ queryKey: ['identity-attributes'], queryFn: () => api<AttributeValues>('/api/identity-attributes'), enabled: open, staleTime: 60_000 })
  const save = useMutation({
    mutationFn: () => api<RoleDetails>(`/api/roles/${roleId}/rules`, { method: 'POST', body: JSON.stringify({ department: department || null, jobTitle: jobTitle || null }) }),
    onSuccess: x => { setDepartment(''); setJobTitle(''); onSaved(x) },
  })
  const general = formError(save.error, ['department', 'jobTitle'])
  return (
    <Modal open={open} title={r.addRule} onClose={onClose} footer={<>
      <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
      <button className="btn primary" disabled={save.isPending || (!department.trim() && !jobTitle.trim())} onClick={() => save.mutate()}>{r.save}</button>
    </>}>
      <div className="form">
        {general && <div className="alert bad">{general}</div>}
        <Field label={r.department} error={fieldError(save.error, 'department')} help={r.ruleHelp}>
          <input className="input" list="dept-values" value={department} onChange={e => setDepartment(e.target.value)} placeholder={r.any} />
        </Field>
        <Field label={r.jobTitle} error={fieldError(save.error, 'jobTitle')}>
          <input className="input" list="title-values" value={jobTitle} onChange={e => setJobTitle(e.target.value)} placeholder={r.any} />
        </Field>
        <datalist id="dept-values">{values.data?.departments.map(v => <option key={v} value={v} />)}</datalist>
        <datalist id="title-values">{values.data?.jobTitles.map(v => <option key={v} value={v} />)}</datalist>
      </div>
    </Modal>
  )
}
