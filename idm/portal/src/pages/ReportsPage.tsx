import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { api, query } from '../api/client'
import type { CorrelationSummary, DriftItem, IdentityListItem, LeaverItem, OrphanAccountItem, PagedResult } from '../api/types'
import { formatDate, formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { ExportButtons } from '../components/ExportButtons'
import { Empty, ErrorBox, PageHead, Pager, Person, SkeletonRows, StatusBadge, Tabs, type TabDef } from '../components/ui'

const PAGE = 25
type Report = 'leavers' | 'orphans' | 'drift' | 'without' | 'not-in-hr'
const r = t.reports

export function ReportsPage() {
  const [params, setParams] = useSearchParams()
  const tab = (['leavers', 'orphans', 'drift', 'without', 'not-in-hr'] as const).find(x => x === params.get('tab')) ?? 'leavers'
  const summary = useQuery({ queryKey: ['reports', 'correlation'], queryFn: () => api<CorrelationSummary>('/api/reports/correlation') })
  const s = summary.data
  const tabs: TabDef<Report>[] = [
    { id: 'leavers', label: r.leavers, count: s?.leaversWithActiveAccounts, bad: true },
    { id: 'orphans', label: r.orphans, count: s?.orphanAccounts },
    { id: 'drift', label: r.drift },
    { id: 'without', label: r.withoutAccounts, count: s?.identitiesWithoutAccounts },
    { id: 'not-in-hr', label: r.notInHr, count: s?.notInHr },
  ]

  return (
    <>
      <PageHead title={r.title} subtitle={r.subtitle} />
      {summary.isError && <div className="card" style={{ marginBottom: 16 }}><ErrorBox error={summary.error} /></div>}
      <section className="card" style={{ marginBottom: 16 }}>
        <div className="card-head"><h2>{t.exports.title}</h2></div>
        <div className="card-body muted" style={{ paddingBottom: 4 }}>{t.exports.subtitle}</div>
        {(['access', 'privileged', 'sod', 'orphans', 'leavers'] as const).map(key => (
          <div key={key} className="list-row">
            <div className="grow"><div className="cell-main">{t.exports.names[key]?.[0]}</div><div className="cell-sub">{t.exports.names[key]?.[1]}</div></div>
            <ExportButtons report={key} />
          </div>
        ))}
      </section>
      <section className="card">
        <Tabs tabs={tabs} current={tab} onSelect={x => setParams({ tab: x }, { replace: true })} />
        {tab === 'leavers' && <Leavers />}
        {tab === 'orphans' && <Orphans />}
        {tab === 'drift' && <Drift />}
        {tab === 'without' && <Identities path="identities-without-accounts" text={r.withoutAccountsText} />}
        {tab === 'not-in-hr' && <Identities path="not-in-hr" text={s && !s.hrConnected ? r.noHr : r.notInHrText} />}
      </section>
    </>
  )
}

function usePaged<T>(path: string, base = '/api/reports') {
  const [skip, setSkip] = useState(0)
  const q = useQuery({
    queryKey: ['reports', path, skip],
    queryFn: () => api<PagedResult<T>>(`${base}/${path}${query({ skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  return { q, skip, setSkip }
}

function Note({ text, bad }: { text: string; bad?: boolean }) {
  return <div className="card-body" style={{ color: bad ? 'var(--bad)' : 'var(--muted)', fontSize: 13.5, borderBottom: '1px solid var(--border)', display: 'flex', gap: 8 }}>
    {bad && <Icon name="warning" size={18} />}<span>{text}</span>
  </div>
}

function Leavers() {
  const { q, skip, setSkip } = usePaged<LeaverItem>('leavers-with-active-accounts')
  return (
    <>
      <Note text={r.leaversText} bad={(q.data?.total ?? 0) > 0} />
      {q.isError ? <ErrorBox error={q.error} onRetry={() => { void q.refetch() }} /> : (
        <>
          <table className="data">
            <thead><tr><th>{t.identities.colName}</th><th>{t.identities.colDepartment}</th><th>{r.colTerminated}</th><th>{r.colAccounts}</th></tr></thead>
            {q.isPending ? <SkeletonRows cols={4} /> : (
              <tbody>
                {q.data?.items.map(i => (
                  <tr key={i.id}>
                    <td><Person name={i.displayName} sub={<span className="mono">{i.employeeNumber}</span>} to={`/identities/${i.id}`} /></td>
                    <td>{i.department ?? '—'}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>{formatDate(i.terminationDate)}</td>
                    <td><div className="chips">{i.activeAccounts.map(a => <span key={a.id} className="chip privileged">{a.applicationCode}: {a.login}</span>)}</div></td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
          {q.data?.total === 0 && <Empty icon="shield" title={r.allGood} />}
          {q.data && <Pager skip={skip} page={PAGE} total={q.data.total} onChange={setSkip} />}
        </>
      )}
    </>
  )
}

function Orphans() {
  const { q, skip, setSkip } = usePaged<OrphanAccountItem>('orphan-accounts')
  return (
    <>
      <Note text={r.orphansText} />
      {q.isError ? <ErrorBox error={q.error} onRetry={() => { void q.refetch() }} /> : (
        <>
          <table className="data">
            <thead><tr><th>{t.applications.colLogin}</th><th>{r.colSystem}</th><th>{t.identities.colNumber}</th><th>{t.identities.colStatus}</th><th>{t.identities.fields.updated}</th></tr></thead>
            {q.isPending ? <SkeletonRows cols={5} /> : (
              <tbody>
                {q.data?.items.map(a => (
                  <tr key={a.id}>
                    <td><div className="cell-main mono">{a.login}</div><div className="cell-sub">{a.displayName ?? ''}</div></td>
                    <td><Link to={`/applications/${encodeURIComponent(a.applicationCode)}`}>{a.applicationName}</Link></td>
                    <td className="mono">{a.employeeNumber ?? '—'}</td>
                    <td><StatusBadge status={a.status} /></td>
                    <td style={{ whiteSpace: 'nowrap' }}>{formatDateTime(a.lastSyncedAt)}</td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
          {q.data?.total === 0 && <Empty icon="shield" title={r.allGood} />}
          {q.data && <Pager skip={skip} page={PAGE} total={q.data.total} onChange={setSkip} />}
        </>
      )}
    </>
  )
}

function Identities({ path, text }: { path: string; text: string }) {
  const { q, skip, setSkip } = usePaged<IdentityListItem>(path)
  return (
    <>
      <Note text={text} />
      {q.isError ? <ErrorBox error={q.error} onRetry={() => { void q.refetch() }} /> : (
        <>
          <table className="data">
            <thead><tr><th>{t.identities.colName}</th><th>{t.identities.colNumber}</th><th>{t.identities.colDepartment}</th><th>{t.identities.colStatus}</th><th>{t.identities.colSource}</th></tr></thead>
            {q.isPending ? <SkeletonRows cols={5} /> : (
              <tbody>
                {q.data?.items.map(i => (
                  <tr key={i.id}>
                    <td><Person name={i.displayName} sub={i.email ?? ''} to={`/identities/${i.id}`} /></td>
                    <td className="mono">{i.employeeNumber}</td>
                    <td><div>{i.department ?? '—'}</div><div className="cell-sub">{i.jobTitle ?? ''}</div></td>
                    <td><StatusBadge status={i.status} /></td>
                    <td><span className="badge neutral">{t.source[i.source]}</span></td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
          {q.data?.total === 0 && <Empty icon="shield" title={r.allGood} />}
          {q.data && <Pager skip={skip} page={PAGE} total={q.data.total} onChange={setSkip} />}
        </>
      )}
    </>
  )
}

function Drift() {
  const { q, skip, setSkip } = usePaged<DriftItem>('access-drift')
  return (
    <>
      <Note text={r.driftText} />
      {q.isError ? <ErrorBox error={q.error} onRetry={() => { void q.refetch() }} /> : (
        <>
          <table className="data">
            <thead><tr><th>{t.identities.colName}</th><th>{t.identities.colDepartment}</th><th>{t.identities.colStatus}</th>
              <th style={{ textAlign: 'right' }}>{t.roles.missing}</th><th style={{ textAlign: 'right' }}>{t.roles.excess}</th></tr></thead>
            {q.isPending ? <SkeletonRows cols={5} /> : (
              <tbody>
                {q.data?.items.map(i => (
                  <tr key={i.identityId}>
                    <td><Person name={i.name} sub={<span className="mono">{i.employeeNumber}</span>} to={`/identities/${i.identityId}`} /></td>
                    <td>{i.department ?? '—'}</td>
                    <td><StatusBadge status={i.status} /></td>
                    <td className="num state-missing" style={{ textAlign: 'right', fontWeight: 600 }}>{i.missing || '—'}</td>
                    <td className="num state-excess" style={{ textAlign: 'right', fontWeight: 600 }}>{i.excess || '—'}</td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
          {q.data?.total === 0 && <Empty icon="shield" title={r.allGood} />}
          {q.data && <Pager skip={skip} page={PAGE} total={q.data.total} onChange={setSkip} />}
        </>
      )}
    </>
  )
}
