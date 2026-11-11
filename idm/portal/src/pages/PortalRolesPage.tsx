import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { PortalRoleItem, PortalRoleName } from '../api/types'
import { formatDate, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Field, Modal, fieldError, formError } from '../components/forms'
import { ConfirmDialog, Empty, ErrorBox, PageHead, Person, SkeletonRows } from '../components/ui'

const v = t.portalAccess
const ROLES: PortalRoleName[] = ['Admin', 'Auditor', 'HrOperator']

/** Portal vakolatlari (IdM administratori, auditor, HR). Berish va olish — ikki kishilik tasdiq bilan. */
export function PortalRolesPage() {
  const isAdmin = useIsAdmin()
  const client = useQueryClient()
  const [granting, setGranting] = useState(false)
  const [revoking, setRevoking] = useState<PortalRoleItem | null>(null)
  const list = useQuery({ queryKey: ['portal-roles'], queryFn: () => api<PortalRoleItem[]>('/api/portal-roles') })
  const revoke = useMutation({
    mutationFn: (x: PortalRoleItem) => api<unknown>('/api/portal-roles/revoke', { method: 'POST', body: JSON.stringify({ login: x.login, role: x.role }) }),
    onSuccess: () => client.invalidateQueries({ queryKey: ['portal-roles'] }),
  })

  return (
    <>
      <PageHead title={v.title} subtitle={v.subtitle}
        actions={isAdmin && <button className="btn primary" onClick={() => setGranting(true)}><Icon name="shield" size={16} />{v.grant}</button>} />
      {revoke.isError && <div className="card" style={{ marginBottom: 16 }}><ErrorBox error={revoke.error} /></div>}
      <section className="card">
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <table className="data">
            <thead><tr><th>{v.colWho}</th><th>{v.colRole}</th><th>{v.colGranted}</th><th aria-label={v.revoke} /></tr></thead>
            {list.isPending ? <SkeletonRows cols={4} /> : (
              <tbody>
                {list.data.map(x => (
                  <tr key={x.id}>
                    <td><Person name={x.name ?? x.login} sub={<span className="mono">{x.login}</span>} /></td>
                    <td><span className={`badge ${x.role === 'Admin' ? 'warn' : 'neutral'} plain`}>{v.role[x.role]}</span></td>
                    <td>{formatDate(x.createdAt)}<div className="cell-sub">{x.createdBy}</div></td>
                    <td style={{ textAlign: 'right' }}>
                      {isAdmin && <button className="btn ghost sm" disabled={revoke.isPending} onClick={() => setRevoking(x)}><Icon name="x" size={14} />{v.revoke}</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
        )}
        {list.data?.length === 0 && <Empty icon="shield" title={v.empty} />}
      </section>
      <Grant open={granting} onClose={() => setGranting(false)} />
      <ConfirmDialog open={!!revoking} title={v.revoke} danger confirmLabel={v.revoke}
        text={revoking ? v.revokeText(revoking.name ?? revoking.login, v.role[revoking.role] ?? revoking.role) : ''}
        onCancel={() => setRevoking(null)} onConfirm={() => { if (revoking) revoke.mutate(revoking); setRevoking(null) }} />
    </>
  )
}

function Grant({ open, onClose }: { open: boolean; onClose: () => void }) {
  const client = useQueryClient()
  const [login, setLogin] = useState('')
  const [role, setRole] = useState<PortalRoleName>('Auditor')
  const save = useMutation({
    mutationFn: () => api<unknown>('/api/portal-roles', { method: 'POST', body: JSON.stringify({ login: login.trim(), role }) }),
    onSuccess: () => { void client.invalidateQueries({ queryKey: ['portal-roles'] }); onClose() },
  })
  // Har ochilishda toza forma
  useEffect(() => { if (open) { setLogin(''); setRole('Auditor'); save.reset() } }, [open])
  const general = formError(save.error, ['login', 'role'])
  return (
    <Modal open={open} title={v.grant} onClose={onClose} footer={<>
      <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
      <button className="btn primary" disabled={save.isPending || !login.trim()} onClick={() => save.mutate()}>{v.grant}</button>
    </>}>
      <div className="form">
        {general && <div className="alert bad">{general}</div>}
        <Field label={v.login} error={fieldError(save.error, 'login')} help={v.loginHelp}>
          <input className="input mono" value={login} onChange={e => setLogin(e.target.value)} autoComplete="off" />
        </Field>
        <Field label={v.colRole} help={v.roleHelp[role]}>
          <select className="input" value={role} onChange={e => setRole(e.target.value as PortalRoleName)}>
            {ROLES.map(r => <option key={r} value={r}>{v.role[r]}</option>)}
          </select>
        </Field>
        <div className="alert warn"><Icon name="shield" size={16} /><span>{v.fourEyes}</span></div>
      </div>
    </Modal>
  )
}
