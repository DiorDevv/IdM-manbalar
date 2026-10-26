import { useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { api, query } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { EntitlementPick, PagedResult, RiskLevel, SodRuleItem, SodViolationItem } from '../api/types'
import { formatDate, t } from '../i18n'
import { Icon } from '../components/Icon'
import { EntitlementPicker, Field, Modal, fieldError, formError, risks } from '../components/forms'
import { ConfirmDialog, Empty, ErrorBox, PageHead, Pager, Person, RiskBadge, SkeletonRows, Tabs } from '../components/ui'

const s = t.sod
const PAGE = 25
type Tab = 'violations' | 'rules'

export function SodPage() {
  const isAdmin = useIsAdmin()
  const [params, setParams] = useSearchParams()
  const tab = (['violations', 'rules'] as const).find(x => x === params.get('tab')) ?? 'violations'
  const [creating, setCreating] = useState(false)
  const rules = useQuery({ queryKey: ['sod-rules'], queryFn: () => api<SodRuleItem[]>('/api/sod/rules') })
  const violationTotal = rules.data?.reduce((sum, x) => sum + x.violationCount, 0)

  return (
    <>
      <PageHead title={s.title} subtitle={s.subtitle} actions={isAdmin && <button className="btn primary" onClick={() => setCreating(true)}><Icon name="sparkle" size={16} />{s.create}</button>} />
      <section className="card">
        <Tabs current={tab} onSelect={(x: Tab) => setParams({ tab: x }, { replace: true })} tabs={[
          { id: 'violations', label: s.tabViolations, count: violationTotal, bad: true },
          { id: 'rules', label: s.tabRules, count: rules.data?.length },
        ]} />
        {tab === 'violations' ? <Violations /> : <Rules rules={rules.data} error={rules.error} pending={rules.isPending} isAdmin={isAdmin} />}
      </section>
      <CreateRule open={creating} onClose={() => setCreating(false)} />
    </>
  )
}

function Violations() {
  const [skip, setSkip] = useState(0)
  const list = useQuery({
    queryKey: ['sod-violations', skip],
    queryFn: () => api<PagedResult<SodViolationItem>>(`/api/sod/violations${query({ skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  if (list.isError) return <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} />
  return (
    <>
      <table className="data">
        <thead><tr><th>{t.identities.colName}</th><th>{s.colRule}</th><th>{s.colConflict}</th></tr></thead>
        {list.isPending ? <SkeletonRows cols={3} /> : (
          <tbody>
            {list.data?.items.map(v => (
              <tr key={`${v.ruleId}-${v.identityId}`}>
                <td><Person name={v.identityName} sub={<><span className="mono">{v.employeeNumber}</span>{v.department ? ` · ${v.department}` : ''}</>} to={`/identities/${v.identityId}`} /></td>
                <td><div className="cell-main">{v.ruleName}</div><div className="cell-sub" style={{ display: 'flex', gap: 6, alignItems: 'center' }}><span className="mono">{v.ruleCode}</span><RiskBadge risk={v.risk} /></div></td>
                <td><Conflict left={v.left} right={v.right} /></td>
              </tr>
            ))}
          </tbody>
        )}
      </table>
      {list.data?.total === 0 && <Empty icon="shield" title={s.noViolations} text={s.noViolationsText} />}
      {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />}
    </>
  )
}

export function Conflict({ left, right }: { left: string[]; right: string[] }) {
  return (
    <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
      {left.map(x => <span key={`l-${x}`} className="chip privileged">{x}</span>)}
      <span className="badge bad plain">×</span>
      {right.map(x => <span key={`r-${x}`} className="chip privileged">{x}</span>)}
    </div>
  )
}

function Rules({ rules, error, pending, isAdmin }: { rules?: SodRuleItem[]; error: unknown; pending: boolean; isAdmin: boolean }) {
  const client = useQueryClient()
  const [deleting, setDeleting] = useState<SodRuleItem | null>(null)
  const remove = useMutation({
    mutationFn: (id: string) => api<void>(`/api/sod/rules/${id}`, { method: 'DELETE' }),
    onSuccess: () => client.invalidateQueries(),
  })
  if (error) return <ErrorBox error={error} />
  if (pending) return <table className="data"><SkeletonRows cols={3} rows={3} /></table>
  if (!rules?.length) return <Empty icon="shield" title={s.noRules} text={s.noRulesText} />
  return (
    <>
      {remove.isError && <ErrorBox error={remove.error} />}
      {rules.map(x => (
        <div className="list-row" key={x.id} style={{ alignItems: 'flex-start' }}>
          <span className="chip-icon" style={{ ['--tone' as string]: 'var(--bad)', ['--tone-soft' as string]: 'var(--bad-soft)' }}><Icon name="shield" size={14} /></span>
          <div className="grow">
            <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
              <span className="cell-main">{x.name}</span><span className="mono muted">{x.code}</span><RiskBadge risk={x.risk} />
              {x.violationCount > 0 && <span className="badge bad">{x.violationCount} {s.colViolations.toLowerCase()}</span>}
            </div>
            {x.description && <div className="cell-sub">{x.description}</div>}
            <div style={{ marginTop: 8 }}><Conflict left={x.left.map(e => `${e.applicationCode}: ${e.name}`)} right={x.right.map(e => `${e.applicationCode}: ${e.name}`)} /></div>
            <div className="cell-sub" style={{ marginTop: 6 }}>{x.createdBy}, {formatDate(x.createdAt)}</div>
          </div>
          {isAdmin && <button className="icon-btn" aria-label={t.roles.delete} title={t.roles.delete} onClick={() => setDeleting(x)}><Icon name="x" size={15} /></button>}
        </div>
      ))}
      <ConfirmDialog open={!!deleting} title={`${t.roles.delete}: ${deleting?.code ?? ''}`} text={s.deleteConfirm} confirmLabel={t.roles.delete} danger
        onCancel={() => setDeleting(null)} onConfirm={() => { if (deleting) remove.mutate(deleting.id); setDeleting(null) }} />
    </>
  )
}

function CreateRule({ open, onClose }: { open: boolean; onClose: () => void }) {
  const client = useQueryClient()
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [risk, setRisk] = useState<RiskLevel>('High')
  const [left, setLeft] = useState<EntitlementPick[]>([])
  const [right, setRight] = useState<EntitlementPick[]>([])
  const reset = () => { setCode(''); setName(''); setDescription(''); setRisk('High'); setLeft([]); setRight([]) }
  const save = useMutation({
    mutationFn: () => api<SodRuleItem>('/api/sod/rules', { method: 'POST', body: JSON.stringify({
      code, name, description: description.trim() || null, risk, left: left.map(x => x.id), right: right.map(x => x.id),
    }) }),
    onSuccess: () => { void client.invalidateQueries(); reset(); onClose() },
  })
  const general = formError(save.error, ['code', 'name', 'description', 'left', 'right'])
  return (
    <Modal open={open} title={s.create} onClose={onClose} wide footer={<>
      <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
      <button className="btn primary" disabled={save.isPending || !left.length || !right.length} onClick={() => save.mutate()}>{t.roles.save}</button>
    </>}>
      <div className="form">
        {general && <div className="alert bad">{general}</div>}
        <div className="form-row">
          <Field label={t.roles.fields.code} error={fieldError(save.error, 'code')} help={t.roles.codeHelp}>
            <input className="input mono" value={code} maxLength={64} onChange={e => setCode(e.target.value.toUpperCase().replace(/\s+/g, '-'))} autoComplete="off" />
          </Field>
          <Field label={t.roles.fields.risk}>
            <select className="select" value={risk} onChange={e => setRisk(e.target.value as RiskLevel)}>{risks.map(x => <option key={x} value={x}>{t.risk[x]}</option>)}</select>
          </Field>
        </div>
        <Field label={t.roles.fields.name} error={fieldError(save.error, 'name')}>
          <input className="input" value={name} maxLength={256} onChange={e => setName(e.target.value)} />
        </Field>
        <Field label={t.roles.fields.description}>
          <textarea className="input" value={description} maxLength={2000} onChange={e => setDescription(e.target.value)} />
        </Field>
        <div className="field">
          <span className="label">{s.sidesHelp}</span>
          {(fieldError(save.error, 'left') ?? fieldError(save.error, 'right')) && <span className="error">{fieldError(save.error, 'left') ?? fieldError(save.error, 'right')}</span>}
          <div className="sides">
            <div className="field"><span className="label">{s.left} · {left.length}</span><EntitlementPicker selected={left} onChange={setLeft} exclude={right.map(x => x.id)} /></div>
            <span className="vs">VS</span>
            <div className="field"><span className="label">{s.right} · {right.length}</span><EntitlementPicker selected={right} onChange={setRight} exclude={left.map(x => x.id)} /></div>
          </div>
        </div>
      </div>
    </Modal>
  )
}
