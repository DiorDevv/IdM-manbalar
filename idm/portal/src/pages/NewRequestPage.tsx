import { useDeferredValue, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { api, query } from '../api/client'
import { useMe } from '../api/me'
import type { CatalogRole, PersonBrief, RequestDetails, RequestPreview } from '../api/types'
import { t } from '../i18n'
import { Icon } from '../components/Icon'
import { Field } from '../components/forms'
import { Empty, ErrorBox, PageHead, RiskBadge } from '../components/ui'
import { Conflict } from './SodPage'

const q = t.requests

export function NewRequestPage() {
  const me = useMe()
  const navigate = useNavigate()
  const [beneficiary, setBeneficiary] = useState<string>('')   // '' — o'zim uchun
  const [roleId, setRoleId] = useState<string | null>(null)
  const [search, setSearch] = useState('')
  const [justification, setJustification] = useState('')
  const [validTo, setValidTo] = useState('')
  const deferred = useDeferredValue(search.trim())

  const team = useQuery({ queryKey: ['me-team'], queryFn: () => api<PersonBrief[]>('/api/me/team'), enabled: !!me.data?.hasTeam })
  const catalog = useQuery({ queryKey: ['catalog', deferred], queryFn: () => api<CatalogRole[]>(`/api/catalog/roles${query({ search: deferred })}`) })
  const preview = useQuery({
    queryKey: ['request-preview', beneficiary, roleId],
    queryFn: () => api<RequestPreview>('/api/requests/preview', { method: 'POST', body: JSON.stringify({ beneficiaryId: beneficiary || null, roleId }) }),
    enabled: !!roleId,
  })
  const submit = useMutation({
    mutationFn: () => api<RequestDetails>('/api/requests', { method: 'POST', body: JSON.stringify({
      beneficiaryId: beneficiary || null, roleId, justification,
      // Sana kun oxirigacha amal qiladi (mahalliy vaqt bo'yicha)
      validTo: validTo ? new Date(`${validTo}T23:59:59`).toISOString() : null,
    }) }),
    onSuccess: r => navigate(`/requests/${r.id}`),
  })

  if (me.data && !me.data.identityId) {
    return <><PageHead title={q.newTitle} /><div className="alert warn"><Icon name="warning" size={18} /><span>{q.noIdentity}</span></div></>
  }
  const role = catalog.data?.find(r => r.id === roleId)
  const p = preview.data
  const ready = !!roleId && p?.canSubmit && justification.trim().length >= 10 && !submit.isPending
  const minDate = new Date(Date.now() + 86_400_000).toISOString().slice(0, 10)
  const maxDate = new Date(Date.now() + 365 * 86_400_000).toISOString().slice(0, 10)

  return (
    <>
      <PageHead crumbs={<><Link to="/requests">{q.title}</Link><Icon name="chevron" size={13} /></>} title={q.newTitle} subtitle={q.newSubtitle} />
      <div className="request-layout">
        <div className="grid">
          {me.data?.hasTeam && (
            <section className="card">
              <div className="card-head"><h2>{q.forWhom}</h2></div>
              <div className="card-body" style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
                <div className="segmented" role="group" aria-label={q.forWhom}>
                  <button aria-pressed={beneficiary === ''} onClick={() => setBeneficiary('')}>{q.me}</button>
                  <button aria-pressed={beneficiary !== ''} onClick={() => setBeneficiary(team.data?.[0]?.id ?? '')} disabled={!team.data?.length}>{t.roles.tabMembers}</button>
                </div>
                {beneficiary !== '' && (
                  <select className="select" value={beneficiary} onChange={e => setBeneficiary(e.target.value)} aria-label={q.forWhom} style={{ minWidth: 260 }}>
                    {team.data?.map(m => <option key={m.id} value={m.id}>{m.name} · {m.employeeNumber}{m.jobTitle ? ` · ${m.jobTitle}` : ''}</option>)}
                  </select>
                )}
              </div>
            </section>
          )}

          <section className="card">
            <div className="card-head"><h2>{q.pickRole}</h2>{role && <span className="muted" style={{ fontSize: 12.5 }}>{role.name}</span>}</div>
            <div className="toolbar">
              <label className="search"><Icon name="search" size={16} />
                <input className="input" placeholder={q.searchRoles} aria-label={q.searchRoles} value={search} onChange={e => setSearch(e.target.value)} />
              </label>
            </div>
            {catalog.isError ? <ErrorBox error={catalog.error} /> : catalog.data?.length === 0 ? <Empty icon="role" title={q.noRoles} /> : (
              <div className="role-grid" role="radiogroup" aria-label={q.pickRole}>
                {catalog.data?.map(r => (
                  <button key={r.id} className="role-card" role="radio" aria-checked={r.id === roleId} onClick={() => setRoleId(r.id)}>
                    <span className="check-dot">{r.id === roleId && <Icon name="check" size={12} strokeWidth={3} />}</span>
                    <span className="title"><Icon name="role" size={15} />{r.name}</span>
                    <span style={{ display: 'flex', gap: 6, alignItems: 'center' }}><span className="mono muted" style={{ fontSize: 11.5 }}>{r.code}</span><RiskBadge risk={r.risk} /></span>
                    {r.description && <span className="desc">{r.description}</span>}
                    <span className="chips">{r.entitlements.slice(0, 4).map(e => <span key={e} className="chip">{e}</span>)}{r.entitlements.length > 4 && <span className="chip">+{r.entitlements.length - 4}</span>}</span>
                  </button>
                ))}
              </div>
            )}
          </section>

          <section className="card">
            <div className="card-body form">
              <Field label={q.justification} help={q.justificationHelp}>
                <textarea className="input" rows={4} maxLength={2000} value={justification} onChange={e => setJustification(e.target.value)} />
              </Field>
              <Field label={q.validTo} help={q.validToHelp}>
                <input className="input" type="date" min={minDate} max={maxDate} value={validTo} onChange={e => setValidTo(e.target.value)} style={{ maxWidth: 220 }} />
              </Field>
            </div>
          </section>
        </div>

        <div className="grid sticky">
          <section className="card">
            <div className="card-head"><h2>{q.chain}</h2></div>
            {!roleId ? <div className="card-body muted" style={{ fontSize: 13 }}>{q.selectRole}</div>
              : preview.isError ? <ErrorBox error={preview.error} />
              : !p ? <div className="card-body"><div className="skeleton" /></div>
              : !p.canSubmit ? <div className="card-body"><div className="alert warn"><Icon name="warning" size={18} /><span>{p.blocker}</span></div></div>
              : (
                <ol className="chain">
                  {p.steps.map((s, i) => (
                    <li key={s.kind} className={s.auto ? 'done' : i === p.steps.findIndex(x => !x.auto) ? 'current' : ''}>
                      <span className="node">{s.auto ? <Icon name="check" size={14} strokeWidth={2.5} /> : i + 1}</span>
                      <div><div className="who">{q.step[s.kind]}</div><div className="meta">{s.approverName ?? q.adminPool}{s.auto ? ` · ${q.auto}` : ''}</div></div>
                    </li>
                  ))}
                </ol>
              )}
            {p && p.sod.length > 0 && (
              <div className="card-body" style={{ borderTop: '1px solid var(--border)' }}>
                <div className="alert bad" style={{ marginBottom: 10 }}><Icon name="shield" size={18} /><span>{q.sodWarning}</span></div>
                {p.sod.map(c => <div key={c.ruleCode} style={{ marginBottom: 8 }}><div className="cell-sub" style={{ marginBottom: 4 }}>{c.ruleName} ({c.ruleCode})</div><Conflict left={c.left} right={c.right} /></div>)}
              </div>
            )}
            <div className="card-body" style={{ borderTop: '1px solid var(--border)', display: 'grid', gap: 10 }}>
              {submit.isError && <div className="alert bad"><Icon name="warning" size={16} /><span>{(submit.error as Error).message}</span></div>}
              <button className="btn primary" disabled={!ready} onClick={() => submit.mutate()}>
                <Icon name="arrow" size={16} />{submit.isPending ? q.submitting : q.submit}
              </button>
            </div>
          </section>
        </div>
      </div>
    </>
  )
}
