import { useEffect, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api, query } from '../api/client'
import type { MoverItem, PagedResult } from '../api/types'
import { formatDate, formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Field, Modal, formError } from '../components/forms'
import { Empty, ErrorBox, Pager, Person, RiskBadge, SkeletonRows } from '../components/ui'

const m = t.movers
const PAGE = 25
const DAY = 86_400_000

/**
 * Boshqa filial yoki bo'limga o'tgan xodimlarning avvalgi joydan qolgan rollari: yangi rahbar muddat ichida
 * "qoldirish" (asos bilan) yoki "olib tashlash" deydi, aks holda rol muddat tugagach avtomatik olinadi.
 */
export function MoversPanel({ scope }: { scope: 'mine' | 'all' }) {
  const [skip, setSkip] = useState(0)
  const [deciding, setDeciding] = useState<{ item: MoverItem; keep: boolean } | null>(null)
  useEffect(() => setSkip(0), [scope])
  const list = useQuery({
    queryKey: ['mover-reviews', scope, skip],
    queryFn: () => api<PagedResult<MoverItem>>(`/api/mover-reviews${query({ scope, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })

  if (list.isError) return <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} />
  return (
    <>
      <div className="card-body muted" style={{ fontSize: 12.5, borderBottom: '1px solid var(--border)' }}>{m.help}</div>
      <table className="data">
        <thead><tr><th>{t.identities.colName}</th><th>{m.colRole}</th><th>{m.colNow}</th><th>{m.colDue}</th><th aria-label={m.colActions} /></tr></thead>
        {list.isPending ? <SkeletonRows cols={5} /> : (
          <tbody>
            {list.data?.items.map(x => {
              const left = Math.ceil((Date.parse(x.dueAt) - Date.now()) / DAY)
              return (
                <tr key={x.assignmentId}>
                  <td><Person name={x.name} sub={<span className="mono">{x.employeeNumber}</span>} to={scope === 'all' ? `/identities/${x.identityId}` : undefined} /></td>
                  <td>
                    <Link to={`/roles/${x.roleId}`} className="cell-main">{x.roleName}</Link>
                    <div className="cell-sub" style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                      <span className="mono">{x.roleCode}</span><RiskBadge risk={x.risk} /><span>{t.roles.source[x.source]}</span>
                    </div>
                  </td>
                  <td><div>{x.department ?? t.noValue}</div><div className="cell-sub">{[x.branch, x.jobTitle].filter(Boolean).join(', ') || t.noValue}</div></td>
                  <td style={{ whiteSpace: 'nowrap' }} title={formatDateTime(x.dueAt)}>
                    <span className={`badge ${left <= 2 ? 'bad' : 'warn'}`}>{formatDate(x.dueAt)}</span>
                    <div className="cell-sub">{left > 0 ? m.daysLeft(left) : m.expiring}</div>
                  </td>
                  <td style={{ textAlign: 'right', whiteSpace: 'nowrap' }}>
                    {x.canDecide ? <>
                      <button className="btn secondary sm" onClick={() => setDeciding({ item: x, keep: true })}><Icon name="check" size={14} />{m.keep}</button>{' '}
                      <button className="btn ghost sm" onClick={() => setDeciding({ item: x, keep: false })}><Icon name="x" size={14} />{m.revoke}</button>
                    </> : <span className="cell-sub">{x.managerName ?? m.noManager}</span>}
                  </td>
                </tr>
              )
            })}
          </tbody>
        )}
      </table>
      {list.data?.total === 0 && <Empty icon="people" title={m.empty} text={m.emptyText} />}
      {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />}
      <Decide value={deciding} onClose={() => setDeciding(null)} />
    </>
  )
}

function Decide({ value, onClose }: { value: { item: MoverItem; keep: boolean } | null; onClose: () => void }) {
  const client = useQueryClient()
  const [comment, setComment] = useState('')
  const save = useMutation({
    mutationFn: () => api<void>(`/api/mover-reviews/${value!.item.assignmentId}`, {
      method: 'POST', body: JSON.stringify({ decision: value!.keep ? 'keep' : 'revoke', comment: comment.trim() || null }),
    }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['mover-reviews'] })
      void client.invalidateQueries({ queryKey: ['me'] })
      onClose()
    },
  })
  // Har ochilishda toza forma
  useEffect(() => { if (value) { setComment(''); save.reset() } }, [value])
  if (!value) return null
  const { item, keep } = value
  const tooShort = keep && comment.trim().length < 5
  return (
    <Modal open title={keep ? m.keepTitle : m.revokeTitle} onClose={onClose} footer={<>
      <button className="btn secondary" onClick={onClose}>{t.cancel}</button>
      <button className={`btn ${keep ? 'primary' : 'danger'}`} disabled={save.isPending || tooShort} onClick={() => save.mutate()}>{keep ? m.keep : m.revoke}</button>
    </>}>
      <div className="form">
        {formError(save.error, []) && <div className="alert bad">{formError(save.error, [])}</div>}
        <p style={{ margin: 0 }}>{keep ? m.keepText(item.name, item.roleName) : m.revokeText(item.name, item.roleName)}</p>
        <Field label={keep ? m.reason : m.comment} help={keep ? m.reasonHelp : undefined}>
          <textarea className="input" maxLength={500} value={comment} onChange={e => setComment(e.target.value)} />
        </Field>
      </div>
    </Modal>
  )
}
