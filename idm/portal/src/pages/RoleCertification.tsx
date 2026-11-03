import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { OwnedRoleItem } from '../api/types'
import { formatDate, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Field } from '../components/forms'
import { Empty, ErrorBox, RiskBadge, SkeletonRows } from '../components/ui'

const v = t.reviews

/** Rol egasi rol tarkibini tasdiqlaydi: keraksiz ruxsatni belgidan chiqaradi (olib tashlash — sabab bilan). */
export function RoleCertification() {
  const roles = useQuery({ queryKey: ['owned-roles'], queryFn: () => api<OwnedRoleItem[]>('/api/me/owned-roles') })
  if (roles.isError) return <ErrorBox error={roles.error} onRetry={() => { void roles.refetch() }} />
  if (roles.isPending) return <table className="data"><SkeletonRows cols={2} rows={3} /></table>
  if (roles.data.length === 0) return <Empty icon="role" title={v.noOwnedRoles} />
  return (
    <>
      <p className="cell-sub" style={{ padding: '12px 16px 0' }}>{v.rolesHelp}</p>
      {roles.data.map(r => <RoleCard key={r.id} role={r} />)}
    </>
  )
}

function RoleCard({ role: r }: { role: OwnedRoleItem }) {
  const client = useQueryClient()
  const [keep, setKeep] = useState<Set<string>>(() => new Set(r.entitlements.map(e => e.id)))
  const [reason, setReason] = useState('')
  const removed = r.entitlements.length - keep.size
  const certify = useMutation({
    mutationFn: () => api<void>(`/api/roles/${r.id}/certify`, { method: 'POST', body: JSON.stringify({ keep: [...keep], comment: reason.trim() || null }) }),
    onSuccess: () => { setReason(''); void client.invalidateQueries({ predicate: q => q.queryKey[0] === 'owned-roles' || q.queryKey[0] === 'me' }) },
  })
  const toggle = (id: string) => setKeep(k => { const n = new Set(k); if (n.has(id)) n.delete(id); else n.add(id); return n })
  return (
    <div className="list-row" style={{ alignItems: 'flex-start' }}>
      <span className="chip-icon"><Icon name="role" size={14} /></span>
      <div className="grow">
        <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          <span className="cell-main">{r.name}</span><span className="mono muted">{r.code}</span><RiskBadge risk={r.risk} />
          <span className="cell-sub">{v.members(r.members)}</span>
          {r.due ? <span className="badge warn">{v.certifyDue}</span> : null}
        </div>
        <div className="cell-sub">{r.certifiedAt ? v.certified(formatDate(r.certifiedAt), r.certifiedBy ?? '') : v.neverCertified}</div>
        {r.rules.length > 0 && <div className="cell-sub">{v.autoRules}: {r.rules.join('; ')}</div>}
        <div style={{ marginTop: 8, display: 'grid', gap: 4 }}>
          {r.entitlements.map(e => (
            <label key={e.id} className="picker-item" style={{ maxWidth: 520 }}>
              <input type="checkbox" checked={keep.has(e.id)} onChange={() => toggle(e.id)} />
              {e.isPrivileged && <span style={{ color: 'var(--warn)' }}><Icon name="lock" size={13} /></span>}
              <span className="name" style={{ textDecoration: keep.has(e.id) ? undefined : 'line-through' }}>{e.name}</span><span className="app-code">{e.applicationCode}</span>
            </label>
          ))}
        </div>
        {removed > 0 && (
          <div style={{ marginTop: 8, maxWidth: 520 }}>
            <Field label={v.removeReason} help={v.removeReasonHelp}>
              <input className="input" value={reason} maxLength={2000} onChange={e => setReason(e.target.value)} />
            </Field>
          </div>
        )}
        {certify.isError && <ErrorBox error={certify.error} />}
        {certify.isSuccess && <div className="alert info" style={{ marginTop: 8 }}>{v.certifyDone}</div>}
        <div style={{ marginTop: 8 }}>
          <button className={`btn ${removed > 0 ? 'danger' : 'primary'}`} disabled={certify.isPending || (removed > 0 && reason.trim().length < 3)}
            onClick={() => certify.mutate()}>{removed > 0 ? v.certifyRemove(removed) : v.certify}</button>
        </div>
      </div>
    </div>
  )
}
