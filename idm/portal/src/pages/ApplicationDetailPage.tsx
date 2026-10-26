import { useDeferredValue, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api, ApiError, query } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { AccountStatus, ApplicationAccount, ApplicationDetails, ConnectorRun, EntitlementItem, PagedResult } from '../api/types'
import { formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { ConfirmDialog, Empty, ErrorBox, PageHead, Pager, Person, RiskBadge, RunStatusBadge, SkeletonRows, Stat, StatusBadge, Tabs } from '../components/ui'

const PAGE = 25
type Tab = 'accounts' | 'groups' | 'runs'
const a = t.applications

export function ApplicationDetailPage() {
  const { code = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const tab = (['accounts', 'groups', 'runs'] as const).find(x => x === params.get('tab')) ?? 'accounts'
  const setTab = (x: Tab) => setParams({ tab: x }, { replace: true })
  const [confirmMassDelete, setConfirmMassDelete] = useState(false)
  const isAdmin = useIsAdmin()
  const client = useQueryClient()
  const path = `/api/applications/${encodeURIComponent(code)}`
  const details = useQuery({
    queryKey: ['application', code],
    queryFn: () => api<ApplicationDetails>(path),
    refetchInterval: q => q.state.data?.isSyncing ? 2000 : false,
  })
  const sync = useMutation({
    mutationFn: (allowMassDelete: boolean) => api<{ runId: string }>(`${path}/sync${query({ allowMassDelete: allowMassDelete ? 'true' : undefined })}`, { method: 'POST' }),
    onSettled: () => client.invalidateQueries({ queryKey: ['application', code] }),
  })
  // Sinxronlash tugaganda jadvallar va boshqa sahifalardagi sonlar yangilanadi
  const [wasSyncing, setWasSyncing] = useState(false)
  if (details.data && details.data.isSyncing !== wasSyncing) {
    setWasSyncing(details.data.isSyncing)
    if (!details.data.isSyncing) void client.invalidateQueries({ predicate: q => q.queryKey[0] !== 'application' || q.queryKey[1] !== code })
  }

  const crumbs = <><Link to="/applications">{t.applications.title}</Link><Icon name="chevron" size={13} /></>
  if (details.isError) {
    return <div className="card">{details.error instanceof ApiError && details.error.status === 404 ? <Empty icon="apps" title={a.notFound} /> : <ErrorBox error={details.error} />}</div>
  }
  if (!details.data) return <div className="crumbs">{crumbs}{t.loading}</div>
  const d = details.data
  const running = d.isSyncing || sync.isPending
  const hr = d.kind === 'HumanResources'
  const aborted = d.lastRun?.status === 'Aborted' ? d.lastRun : null

  return (
    <>
      <PageHead crumbs={<>{crumbs}{d.name}</>} title={d.name}
        subtitle={<span style={{ display: 'inline-flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
          <span className="mono">{d.code}</span> · {t.kind[d.kind] ?? d.kind} · <RiskBadge risk={d.risk} />
          {d.hasConnector && <> · {d.intervalMinutes ? a.every(d.intervalMinutes) : a.manualOnly}</>}
        </span>}
        actions={isAdmin && d.hasConnector && (
          <button className="btn primary" disabled={running} onClick={() => sync.mutate(false)}>
            <Icon name="sync" size={16} />{running ? a.syncing : a.sync}
          </button>
        )} />

      {sync.isError && <div style={{ marginBottom: 16 }}><div className="card"><ErrorBox error={sync.error} /></div></div>}
      {aborted && !running && (
        <div className="alert warn" style={{ marginBottom: 16 }}>
          <Icon name="warning" size={18} />
          <div className="grow"><strong>{a.abortedTitle}</strong><div style={{ marginTop: 4 }}>{aborted.error}</div></div>
          {isAdmin && <button className="btn secondary sm" onClick={() => setConfirmMassDelete(true)}>{a.approveDelete}</button>}
        </div>
      )}

      <ConfirmDialog open={confirmMassDelete} title={a.approveDelete} text={a.approveConfirm} confirmLabel={a.approveDelete} danger
        onCancel={() => setConfirmMassDelete(false)} onConfirm={() => { setConfirmMassDelete(false); sync.mutate(true) }} />

      {hr ? (
        <div className="grid stats">
          <Stat icon="clock" label={a.colLastSync} value={d.lastSuccessAt ? t.ago(d.lastSuccessAt) : '—'} small hint={d.lastSuccessAt ? formatDateTime(d.lastSuccessAt) : undefined} />
          {d.lastRun && <Stat icon="sync" label={a.colResult} value={a.runSummary(d.lastRun, true)} small hint={a.hrChanges(d.lastRun)} />}
        </div>
      ) : <div className="grid stats">
        <Stat icon="user" label={a.enabled} value={d.enabledAccounts} />
        <Stat icon="lock" label={a.disabled} value={d.disabledAccounts} />
        <Stat icon="warning" label={a.orphans} value={d.orphanAccounts} attention={d.orphanAccounts > 0} hint={t.dashboard.orphanHint} />
        <Stat icon="people" label={a.groups} value={d.entitlements} hint={d.privilegedEntitlements ? `${d.privilegedEntitlements} ${a.privileged}` : undefined} />
        <Stat icon="clock" label={a.colLastSync} value={d.lastSuccessAt ? t.ago(d.lastSuccessAt) : '—'} small hint={d.lastSuccessAt ? formatDateTime(d.lastSuccessAt) : undefined} />
      </div>}

      {hr ? <section className="card"><div className="card-head"><h2>{a.tabRuns}</h2></div><RunsTab path={path} code={code} live={d.isSyncing} hr /></section> : <section className="card">
        <Tabs current={tab} onSelect={setTab} tabs={[
          { id: 'accounts', label: a.tabAccounts, count: d.enabledAccounts + d.disabledAccounts },
          { id: 'groups', label: a.tabGroups, count: d.entitlements },
          { id: 'runs', label: a.tabRuns },
        ]} />
        {tab === 'accounts' && <AccountsTab path={path} />}
        {tab === 'groups' && <GroupsTab path={path} />}
        {tab === 'runs' && <RunsTab path={path} code={code} live={d.isSyncing} />}
      </section>}
    </>
  )
}

function AccountsTab({ path }: { path: string }) {
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<AccountStatus | ''>('')
  const [orphan, setOrphan] = useState(false)
  const [skip, setSkip] = useState(0)
  const deferred = useDeferredValue(search)
  const list = useQuery({
    queryKey: ['application-accounts', path, deferred, status, orphan, skip],
    queryFn: () => api<PagedResult<ApplicationAccount>>(`${path}/accounts${query({ search: deferred, status, orphan: orphan ? 'true' : undefined, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  return (
    <>
      <div className="toolbar">
        <label className="search"><Icon name="search" size={18} /><input className="input" placeholder={a.searchAccounts} aria-label={a.searchAccounts} value={search} onChange={e => { setSearch(e.target.value); setSkip(0) }} /></label>
        <select className="select" value={status} onChange={e => { setStatus(e.target.value as AccountStatus | ''); setSkip(0) }} aria-label={t.identities.colStatus}>
          <option value="">{a.allStatuses}</option>
          {(['Enabled', 'Disabled', 'Deleted'] as const).map(s => <option key={s} value={s}>{t.status[s]}</option>)}
        </select>
        <label className="check"><input type="checkbox" checked={orphan} onChange={e => { setOrphan(e.target.checked); setSkip(0) }} />{a.onlyOrphans}</label>
      </div>
      {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
        <>
          <table className="data">
            <thead><tr><th>{a.colLogin}</th><th>{t.identities.colNumber}</th><th>{a.colIdentity}</th><th>{t.identities.colStatus}</th><th style={{ textAlign: 'right' }}>{a.colGroups}</th><th>{t.identities.fields.updated}</th></tr></thead>
            {list.isPending ? <SkeletonRows cols={6} /> : (
              <tbody>
                {list.data?.items.map(x => (
                  <tr key={x.id}>
                    <td><div className="cell-main mono">{x.login}</div><div className="cell-sub">{x.displayName ?? x.email ?? ''}</div></td>
                    <td className="mono">{x.employeeNumber ?? '—'}</td>
                    <td>{x.identityId ? <Person name={x.identityName ?? ''} to={`/identities/${x.identityId}`} />
                      : <span className="badge warn">{a.noIdentity}</span>}</td>
                    <td><StatusBadge status={x.status} /></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{x.entitlementCount}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>{formatDateTime(x.lastSyncedAt)}</td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
          {list.data?.total === 0 && <Empty icon="people" title={a.emptyAccounts} />}
          {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />}
        </>
      )}
    </>
  )
}

function GroupsTab({ path }: { path: string }) {
  const [search, setSearch] = useState('')
  const [skip, setSkip] = useState(0)
  const deferred = useDeferredValue(search)
  const list = useQuery({
    queryKey: ['application-groups', path, deferred, skip],
    queryFn: () => api<PagedResult<EntitlementItem>>(`${path}/entitlements${query({ search: deferred, skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
  })
  return (
    <>
      <div className="toolbar">
        <label className="search"><Icon name="search" size={18} /><input className="input" placeholder={a.searchGroups} aria-label={a.searchGroups} value={search} onChange={e => { setSearch(e.target.value); setSkip(0) }} /></label>
      </div>
      {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
        <>
          <table className="data">
            <thead><tr><th>{a.colGroup}</th><th>{t.applications.colRisk}</th><th style={{ textAlign: 'right' }}>{a.colMembers}</th><th>{t.identities.fields.updated}</th></tr></thead>
            {list.isPending ? <SkeletonRows cols={4} /> : (
              <tbody>
                {list.data?.items.map(g => (
                  <tr key={g.id}>
                    <td>
                      <div className="cell-main" style={{ display: 'flex', alignItems: 'center', gap: 6 }}>{g.isPrivileged && <span style={{ color: 'var(--warn)' }} title={a.privileged}><Icon name="lock" size={14} /></span>}{g.name}</div>
                      <div className="cell-sub">{g.description ?? ''}</div>
                    </td>
                    <td><RiskBadge risk={g.risk} /></td>
                    <td style={{ textAlign: 'right', fontVariantNumeric: 'tabular-nums' }}>{g.memberCount}</td>
                    <td style={{ whiteSpace: 'nowrap' }}>{formatDateTime(g.lastSyncedAt)}</td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
          {list.data?.total === 0 && <Empty icon="role" title={a.emptyGroups} />}
          {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />}
        </>
      )}
    </>
  )
}

function RunsTab({ path, code, live, hr = false }: { path: string; code: string; live: boolean; hr?: boolean }) {
  const [skip, setSkip] = useState(0)
  const list = useQuery({
    queryKey: ['application-runs', code, skip],
    queryFn: () => api<PagedResult<ConnectorRun>>(`${path}/runs${query({ skip, take: PAGE })}`),
    placeholderData: keepPreviousData,
    refetchInterval: live ? 2000 : false,
  })
  return list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
    <>
      <table className="data">
        <thead><tr><th>{a.colStarted}</th><th>{a.colTrigger}</th><th>{t.identities.colStatus}</th><th>{a.colResult}</th><th style={{ textAlign: 'right' }}>{a.colDuration}</th></tr></thead>
        {list.isPending ? <SkeletonRows cols={5} /> : (
          <tbody>
            {list.data?.items.map(r => (
              <tr key={r.id}>
                <td style={{ whiteSpace: 'nowrap' }}>{formatDateTime(r.startedAt)}</td>
                <td><div>{a.trigger[r.trigger]}</div><div className="cell-sub">{r.trigger === 'Manual' ? r.triggeredBy : ''}</div></td>
                <td><RunStatusBadge status={r.status} /></td>
                <td style={{ maxWidth: 520 }}>
                  {r.status !== 'Running' && <div>{a.runSummary(r, hr)}</div>}
                  {r.status === 'Succeeded' && <div className="cell-sub">{hr ? a.hrChanges(r) : a.runChanges(r)}{!hr && r.orphanAccounts ? ` · ${r.orphanAccounts} ${a.orphans.toLowerCase()}` : ''}</div>}
                  {r.error && <div className="run-error">{r.error}</div>}
                </td>
                <td style={{ textAlign: 'right', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
                  {r.finishedAt ? t.duration(new Date(r.finishedAt).getTime() - new Date(r.startedAt).getTime()) : '…'}
                </td>
              </tr>
            ))}
          </tbody>
        )}
      </table>
      {list.data?.total === 0 && <Empty icon="clock" title={a.emptyRuns} text={a.emptyRunsText} />}
      {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={setSkip} />}
    </>
  )
}
