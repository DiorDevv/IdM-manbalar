import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { BirthrightResult, RoleDetails, RoleListItem } from '../api/types'
import { t } from '../i18n'
import { Icon } from '../components/Icon'
import { Empty, ErrorBox, PageHead, RiskBadge, SkeletonRows } from '../components/ui'
import { RoleForm } from './RoleForm'

const r = t.roles

export function RolesPage() {
  const navigate = useNavigate()
  const isAdmin = useIsAdmin()
  const client = useQueryClient()
  const [creating, setCreating] = useState(false)
  const list = useQuery({ queryKey: ['roles'], queryFn: () => api<RoleListItem[]>('/api/roles') })
  const recalc = useMutation({
    mutationFn: () => api<BirthrightResult>('/api/roles/recalculate', { method: 'POST' }),
    onSuccess: () => client.invalidateQueries(),
  })

  return (
    <>
      <PageHead title={r.title} subtitle={r.subtitle} actions={isAdmin && <>
        <button className="btn secondary" disabled={recalc.isPending} onClick={() => recalc.mutate()}><Icon name="sync" size={16} />{r.recalc}</button>
        <button className="btn primary" onClick={() => setCreating(true)}><Icon name="sparkle" size={16} />{r.create}</button>
      </>} />
      {recalc.data && <div className="alert ok" style={{ marginBottom: 16 }}><Icon name="check" size={18} /><span>{r.recalcDone(recalc.data)}</span></div>}
      {recalc.isError && <div className="card" style={{ marginBottom: 16 }}><ErrorBox error={recalc.error} /></div>}
      <section className="card">
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>{r.colRole}</th><th>{t.applications.colRisk}</th><th style={{ textAlign: 'right' }}>{r.colEntitlements}</th><th style={{ textAlign: 'right' }}>{r.colRules}</th><th style={{ textAlign: 'right' }}>{r.colMembers}</th></tr></thead>
              {list.isPending ? <SkeletonRows cols={5} rows={4} /> : (
                <tbody>
                  {list.data?.map(x => (
                    <tr key={x.id} className="clickable" onClick={() => navigate(`/roles/${x.id}`)}>
                      <td>
                        <div className="person">
                          <span className="chip-icon" style={{ ['--tone' as string]: 'var(--violet)', ['--tone-soft' as string]: 'var(--violet-soft)' }}><Icon name="role" size={15} /></span>
                          <div><div className="cell-main">{x.name}</div><div className="cell-sub"><span className="mono">{x.code}</span>{x.description ? ` · ${x.description}` : ''}</div></div>
                        </div>
                      </td>
                      <td><RiskBadge risk={x.risk} /></td>
                      <td className="num" style={{ textAlign: 'right' }}>{x.entitlementCount}</td>
                      <td className="num" style={{ textAlign: 'right' }}>{x.ruleCount}</td>
                      <td className="num" style={{ textAlign: 'right' }}>{x.memberCount}</td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
            {list.data?.length === 0 && <Empty icon="role" title={r.emptyTitle} text={r.emptyText} />}
          </>
        )}
      </section>
      <RoleForm open={creating} onClose={() => setCreating(false)}
        onSaved={(role: RoleDetails) => { setCreating(false); void client.invalidateQueries({ queryKey: ['roles'] }); navigate(`/roles/${role.id}`) }} />
    </>
  )
}
