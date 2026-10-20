import { useState, useDeferredValue } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api, query } from '../api/client'
import type { IdentityListItem, IdentityStatus, PagedResult } from '../api/types'
import { t } from '../i18n'
import { Icon } from '../components/Icon'
import { Empty, ErrorBox, PageHead, SkeletonRows, StatusBadge } from '../components/ui'

const PAGE = 25
const statuses: IdentityStatus[] = ['Active', 'Pending', 'Suspended', 'Terminated']

export function IdentitiesPage() {
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<IdentityStatus | ''>('')
  const [skip, setSkip] = useState(0)
  const deferred = useDeferredValue(search)
  const list = useQuery({
    queryKey: ['identities', deferred, status, skip],
    queryFn: () => api<PagedResult<IdentityListItem>>(`/api/identities${query({ search: deferred, status, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  const data = list.data

  return (
    <>
      <PageHead title={t.identities.title} subtitle={t.identities.subtitle} />
      <section className="card">
        <div className="toolbar">
          <label className="search">
            <Icon name="search" size={18} />
            <input className="input" placeholder={t.identities.search} value={search} onChange={e => { setSearch(e.target.value); setSkip(0) }} aria-label={t.identities.search} />
          </label>
          <select className="select" value={status} onChange={e => { setStatus(e.target.value as IdentityStatus | ''); setSkip(0) }} aria-label={t.identities.colStatus}>
            <option value="">{t.identities.allStatuses}</option>
            {statuses.map(s => <option key={s} value={s}>{t.status[s]}</option>)}
          </select>
        </div>
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>{t.identities.colName}</th><th>{t.identities.colNumber}</th><th>{t.identities.colDepartment}</th><th>{t.identities.colStatus}</th><th>{t.identities.colSource}</th><th style={{ textAlign: 'right' }}>{t.identities.colAccounts}</th></tr></thead>
              {list.isPending ? <SkeletonRows cols={6} /> : (
                <tbody>
                  {data?.items.map(i => (
                    <tr key={i.id} className="clickable" onClick={() => navigate(`/identities/${i.id}`)}>
                      <td><div className="cell-main">{i.displayName}</div><div className="cell-sub">{i.email ?? '—'}</div></td>
                      <td className="mono">{i.employeeNumber}</td>
                      <td><div>{i.department ?? '—'}</div><div className="cell-sub">{i.jobTitle ?? ''}</div></td>
                      <td><StatusBadge status={i.status} /></td>
                      <td><span className="badge neutral">{t.source[i.source]}</span></td>
                      <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{i.accountCount}</td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
            {data && data.total === 0 && <Empty title={t.identities.emptyTitle} text={t.identities.emptyText} />}
            {data && data.total > 0 && (
              <div className="pager">
                <span>{t.identities.shown(skip + 1, Math.min(skip + PAGE, data.total), data.total)}</span>
                <span style={{ display: 'flex', gap: 8 }}>
                  <button className="btn secondary" disabled={skip === 0} onClick={() => setSkip(Math.max(0, skip - PAGE))}>{t.identities.prev}</button>
                  <button className="btn secondary" disabled={skip + PAGE >= data.total} onClick={() => setSkip(skip + PAGE)}>{t.identities.next}</button>
                </span>
              </div>
            )}
          </>
        )}
      </section>
    </>
  )
}
