import { useState, useDeferredValue } from 'react'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { api, query } from '../api/client'
import type { IdentityListItem, IdentityStatus, PagedResult } from '../api/types'
import { t } from '../i18n'
import { Icon } from '../components/Icon'
import { Empty, ErrorBox, PageHead, Pager, Person, SkeletonRows, StatusBadge } from '../components/ui'

const PAGE = 25
const statuses: IdentityStatus[] = ['Active', 'Pending', 'Suspended', 'Terminated']

export function IdentitiesPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const status = statuses.find(s => s === params.get('status')) ?? ''
  const [search, setSearch] = useState('')
  const [skip, setSkip] = useState(0)
  const deferred = useDeferredValue(search)
  const list = useQuery({
    queryKey: ['identities', deferred, status, skip],
    queryFn: () => api<PagedResult<IdentityListItem>>(`/api/identities${query({ search: deferred, status, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  const data = list.data
  const setStatus = (s: IdentityStatus | '') => { setSkip(0); setParams(s ? { status: s } : {}, { replace: true }) }

  return (
    <>
      <PageHead title={t.identities.title} subtitle={t.identities.subtitle} />
      <section className="card">
        <div className="toolbar">
          <label className="search">
            <Icon name="search" size={16} />
            <input className="input" placeholder={t.identities.search} value={search} onChange={e => { setSearch(e.target.value); setSkip(0) }} aria-label={t.identities.search} />
          </label>
          <select className="select" value={status} onChange={e => setStatus(e.target.value as IdentityStatus | '')} aria-label={t.identities.colStatus}>
            <option value="">{t.identities.allStatuses}</option>
            {statuses.map(s => <option key={s} value={s}>{t.status[s]}</option>)}
          </select>
          {data && <span className="muted" style={{ fontSize: 12.5, marginLeft: 4 }}>{t.identities.found(data.total)}</span>}
        </div>
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <>
            <table className="data">
              <thead><tr><th>{t.identities.colName}</th><th>{t.identities.colNumber}</th><th>{t.identities.colDepartment}</th><th>{t.identities.colStatus}</th><th>{t.identities.colSource}</th><th style={{ textAlign: 'right' }}>{t.identities.colAccounts}</th></tr></thead>
              {list.isPending ? <SkeletonRows cols={6} /> : (
                <tbody>
                  {data?.items.map(i => (
                    <tr key={i.id} className="clickable" onClick={() => navigate(`/identities/${i.id}`)}>
                      <td><Person name={i.displayName} sub={i.email ?? '—'} to={`/identities/${i.id}`} /></td>
                      <td className="mono">{i.employeeNumber}</td>
                      <td><div>{i.department ?? '—'}</div><div className="cell-sub">{i.jobTitle ?? ''}</div></td>
                      <td><StatusBadge status={i.status} /></td>
                      <td><span className="badge neutral plain">{t.source[i.source]}</span></td>
                      <td className="num" style={{ textAlign: 'right' }}>{i.accountCount}</td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
            {data && data.total === 0 && <Empty title={t.identities.emptyTitle} text={t.identities.emptyText} />}
            {data && <Pager skip={skip} page={PAGE} total={data.total} onChange={setSkip} />}
          </>
        )}
      </section>
    </>
  )
}
