import { useDeferredValue, useState } from 'react'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { api, query } from '../api/client'
import { useIsAdmin } from '../api/me'
import type { PagedResult, PlanResult, ProvisioningActionResult, ProvisioningApplicationStatus, ProvisioningKind, ProvisioningOperationItem, ProvisioningStatus } from '../api/types'
import { formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { ConfirmDialog, Empty, ErrorBox, PageHead, Pager, Person, SkeletonRows, Stat, Tabs, type TabDef } from '../components/ui'

const p = t.provisioning
const PAGE = 50
type Tab = 'proposed' | 'queued' | 'failed' | 'done' | 'all'
const tabStatuses: Record<Tab, ProvisioningStatus[]> = {
  proposed: ['Proposed'], queued: ['Queued', 'Running'], failed: ['Failed'], done: ['Succeeded'], all: [],
}
const statusTone: Record<ProvisioningStatus, string> = {
  Proposed: 'warn', Queued: 'info', Running: 'info live', Succeeded: 'ok', Failed: 'bad', Obsolete: 'neutral', Cancelled: 'neutral',
}
const kindIcon: Record<ProvisioningKind, string> = { CreateAccount: 'user', DisableAccount: 'lock', AddMembership: 'people', RemoveMembership: 'x' }
/** Xavfli amallar (kirishni olib qo'yadi) tasdiqlash oynasida qizil tugma bilan. */
const dangerous: ProvisioningKind[] = ['DisableAccount', 'RemoveMembership']

export function ProvisioningPage() {
  const isAdmin = useIsAdmin()
  const client = useQueryClient()
  const [params, setParams] = useSearchParams()
  const tab = (Object.keys(tabStatuses) as Tab[]).find(x => x === params.get('tab')) ?? 'proposed'
  const [skip, setSkip] = useState(0)
  const [search, setSearch] = useState('')
  const deferredSearch = useDeferredValue(search.trim())
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [confirm, setConfirm] = useState<'approve' | 'dismiss' | null>(null)
  const [planResults, setPlanResults] = useState<Record<string, PlanResult>>({})
  const [notice, setNotice] = useState<string | null>(null)

  const status = useQuery({
    queryKey: ['provisioning', 'status'],
    queryFn: () => api<ProvisioningApplicationStatus[]>('/api/provisioning/status'),
    refetchInterval: 15_000,
  })
  const statuses = tabStatuses[tab].map(s => `&status=${s}`).join('')
  const list = useQuery({
    queryKey: ['provisioning', 'operations', tab, skip, deferredSearch],
    queryFn: () => api<PagedResult<ProvisioningOperationItem>>(`/api/provisioning/operations${query({ skip, take: PAGE, search: deferredSearch }) || '?'}${statuses}`),
    placeholderData: keepPreviousData,
    refetchInterval: tab === 'queued' ? 5_000 : 15_000,
  })
  const refresh = () => client.invalidateQueries({ queryKey: ['provisioning'] })

  const plan = useMutation({
    mutationFn: (code: string) => api<PlanResult>(`/api/provisioning/${encodeURIComponent(code)}/plan`, { method: 'POST' }),
    onSuccess: (result, code) => setPlanResults(r => ({ ...r, [code]: result })),
    onSettled: refresh,
  })
  const act = useMutation({
    mutationFn: (action: 'approve' | 'retry' | 'dismiss') =>
      api<ProvisioningActionResult>(`/api/provisioning/operations/${action}`, { method: 'POST', body: JSON.stringify({ ids: [...selected] }) }),
    onSuccess: r => { setNotice(p.done(r.changed, r.skipped)); setSelected(new Set()) },
    onSettled: refresh,
  })

  const apps = status.data ?? []
  const total = (key: 'proposed' | 'queued' | 'running' | 'failed' | 'succeededLast24h') => apps.reduce((sum, a) => sum + a[key], 0)
  const tabs: TabDef<Tab>[] = [
    { id: 'proposed', label: p.tabProposed, count: total('proposed') },
    { id: 'queued', label: p.tabQueued, count: total('queued') + total('running') },
    { id: 'failed', label: p.tabFailed, count: total('failed'), bad: true },
    { id: 'done', label: p.tabDone },
    { id: 'all', label: p.tabAll },
  ]
  const selectable = isAdmin && (tab === 'proposed' || tab === 'failed')
  const items = list.data?.items ?? []
  const chosen = items.filter(o => selected.has(o.id))
  const allChosen = items.length > 0 && chosen.length === items.length
  const toggle = (id: string) => setSelected(s => { const next = new Set(s); if (!next.delete(id)) next.add(id); return next })
  const switchTab = (x: Tab) => { setSkip(0); setSelected(new Set()); setNotice(null); setParams({ tab: x }, { replace: true }) }
  const countsByKind = (Object.keys(p.count) as ProvisioningKind[])
    .map(k => [k, chosen.filter(o => o.kind === k).length] as const).filter(([, n]) => n > 0)
  const empty = { proposed: [p.emptyProposed, p.emptyProposedText], queued: [p.emptyQueued], failed: [p.emptyFailed], done: [p.emptyDone], all: [p.emptyAll] }[tab]

  return (
    <>
      <PageHead title={p.title} subtitle={p.subtitle} />

      <div className="grid stats">
        <Stat icon="review" label={p.statProposed} value={total('proposed')} tone="warn" attention={total('proposed') > 0} />
        <Stat icon="clock" label={p.statQueued} value={total('queued') + total('running')} tone="sky" />
        <Stat icon="warning" label={p.statFailed} value={total('failed')} tone="bad" attention={total('failed') > 0} />
        <Stat icon="check" label={p.statDone} value={total('succeededLast24h')} tone="ok" />
      </div>

      <section className="card" style={{ marginBottom: 16 }}>
        <div className="card-head"><h2>{p.systems}</h2></div>
        {status.isError ? <ErrorBox error={status.error} onRetry={() => { void status.refetch() }} /> : status.isPending ? <SkeletonRows rows={1} cols={3} /> : (
          apps.length === 0 ? <div className="alert warn" style={{ margin: 16 }}><Icon name="warning" size={18} /><span>{p.noneEnabled}</span></div> : apps.map(a => {
            const result = planResults[a.code]
            return (
              <div key={a.code} className="list-row" style={{ alignItems: 'flex-start' }}>
                <div className="grow">
                  <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
                    <Link to={`/applications/${encodeURIComponent(a.code)}`} className="cell-main">{a.name}</Link>
                    <span className="mono muted">{a.code}</span>
                    <span className={`badge ${a.mode === 'Off' ? 'neutral' : a.mode === 'Automatic' ? 'ok' : 'warn'}`}>{p.mode[a.mode]}</span>
                  </div>
                  <div className="cell-sub">
                    {p.modeHelp[a.mode]}
                    {a.mode === 'Automatic' && <> · {p.limit(a.maxAutoOperations)}</>}
                    {a.createAccounts && <> · {p.createAccounts}</>}
                  </div>
                  {result && (
                    <div className={`alert ${result.skipped || result.held ? 'warn' : 'info'}`} style={{ marginTop: 10 }}>
                      <Icon name={result.skipped || result.held ? 'warning' : 'check'} size={16} />
                      <div className="grow">
                        <div>{result.skipped ?? p.planResult(result.planned, result.created, result.obsoleted)}</div>
                        {result.held && <div style={{ marginTop: 4 }}>{p.held}</div>}
                        {result.warnings.length > 0 && (
                          <ul style={{ margin: '6px 0 0', paddingLeft: 18 }}>{result.warnings.slice(0, 10).map(w => <li key={w} className="cell-sub">{w}</li>)}</ul>
                        )}
                      </div>
                    </div>
                  )}
                </div>
                {isAdmin && a.mode !== 'Off' && (
                  <button className="btn secondary sm" disabled={plan.isPending} onClick={() => plan.mutate(a.code)}>
                    <Icon name="sync" size={15} />{plan.isPending && plan.variables === a.code ? p.planning : p.plan}
                  </button>
                )}
              </div>
            )
          })
        )}
        {plan.isError && <ErrorBox error={plan.error} />}
      </section>

      <section className="card">
        <Tabs tabs={tabs} current={tab} onSelect={switchTab} />
        <div className="toolbar">
          <label className="search"><Icon name="search" size={18} />
            <input className="input" placeholder={p.search} aria-label={p.search} value={search} onChange={e => { setSearch(e.target.value); setSkip(0) }} />
          </label>
          {selectable && chosen.length > 0 && (
            <>
              <span className="muted" style={{ marginLeft: 'auto' }}>{p.selected(chosen.length)}</span>
              <button className="btn ghost sm" onClick={() => setSelected(new Set())}>{p.clear}</button>
              {tab === 'proposed' && (
                <button className="btn primary sm" disabled={act.isPending} onClick={() => setConfirm('approve')}><Icon name="check" size={15} />{p.approve}</button>
              )}
              {tab === 'failed' && (
                <>
                  <button className="btn secondary sm" disabled={act.isPending} onClick={() => setConfirm('dismiss')}>{p.dismiss}</button>
                  <button className="btn primary sm" disabled={act.isPending} onClick={() => act.mutate('retry')}><Icon name="sync" size={15} />{p.retry}</button>
                </>
              )}
            </>
          )}
        </div>
        {notice && <div className="alert info" style={{ margin: '12px 16px 0' }}><Icon name="check" size={16} /><span>{notice}</span></div>}
        {act.isError && <ErrorBox error={act.error} />}
        {list.isError ? <ErrorBox error={list.error} onRetry={() => { void list.refetch() }} /> : (
          <>
            <div className="table-wrap">
              <table className="data">
                <thead>
                  <tr>
                    {selectable && (
                      <th style={{ width: 36 }}>
                        <input type="checkbox" aria-label={p.selectAll} checked={allChosen} disabled={items.length === 0}
                          onChange={() => setSelected(allChosen ? new Set() : new Set(items.map(o => o.id)))} />
                      </th>
                    )}
                    <th>{p.colAction}</th><th>{p.colWho}</th><th>{p.colTarget}</th><th>{p.colStatus}</th><th>{p.colWhen}</th>
                  </tr>
                </thead>
                {list.isPending ? <SkeletonRows cols={selectable ? 6 : 5} /> : (
                  <tbody>
                    {items.map(o => (
                      <tr key={o.id} className={selected.has(o.id) ? 'selected' : undefined}>
                        {selectable && (
                          <td><input type="checkbox" aria-label={`${p.kind[o.kind]}: ${o.login ?? o.identityName ?? ''}`} checked={selected.has(o.id)} onChange={() => toggle(o.id)} /></td>
                        )}
                        <td>
                          <div className="cell-main" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
                            <Icon name={kindIcon[o.kind]} size={15} />{p.kind[o.kind]}
                          </div>
                          <div className="cell-sub"><span className="mono">{o.applicationCode}</span> · {p.reason[o.reason] ?? o.reason}</div>
                        </td>
                        <td>
                          {o.identityName
                            ? <Person name={o.identityName} to={o.identityId ? `/identities/${o.identityId}` : undefined} sub={<span className="mono">{o.employeeNumber}</span>} />
                            : <span className="muted">—</span>}
                        </td>
                        <td>
                          <div className="mono">{o.login ?? (o.kind === 'CreateAccount' && o.summary ? p.newLogin(o.summary) : '—')}</div>
                          {o.group && (
                            <div style={{ marginTop: 4 }}>
                              <span className={`chip${o.groupPrivileged ? ' privileged' : ''}`}>{o.groupPrivileged && <Icon name="lock" size={12} />}{o.group}</span>
                            </div>
                          )}
                          {o.kind === 'AddMembership' && o.summary && <div className="cell-sub">{p.via}: {o.summary}</div>}
                        </td>
                        <td>
                          <span className={`badge ${statusTone[o.status]}`}>{p.status[o.status]}</span>
                          {o.attempts > 0 && o.status !== 'Succeeded' && <div className="cell-sub">{p.attempt(o.attempts)}</div>}
                          {o.lastError && <div className="cell-sub" style={{ color: 'var(--bad)', maxWidth: 320 }}>{o.lastError}</div>}
                          {o.status === 'Queued' && o.nextAttemptAt && o.attempts > 0 && (
                            <div className="cell-sub">{p.nextAttempt}: {formatDateTime(o.nextAttemptAt)}</div>
                          )}
                        </td>
                        <td style={{ whiteSpace: 'nowrap' }} title={formatDateTime(o.completedAt ?? o.createdAt)}>
                          {t.ago(o.completedAt ?? o.createdAt)}
                          {o.approvedBy && <div className="cell-sub">{p.approvedBy}: {o.approvedByName ?? o.approvedBy}</div>}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                )}
              </table>
            </div>
            {list.data?.total === 0 && <Empty icon="sync" title={empty[0] ?? ''} text={empty[1] ?? ''} />}
            {list.data && <Pager skip={skip} page={PAGE} total={list.data.total} onChange={x => { setSkip(x); setSelected(new Set()) }} />}
          </>
        )}
      </section>

      <ConfirmDialog open={confirm === 'approve'} title={p.confirmApproveTitle} danger={chosen.some(o => dangerous.includes(o.kind))}
        text={p.confirmApprove(countsByKind.map(([k, n]) => p.count[k]?.(n) ?? ''))} confirmLabel={p.approve}
        onCancel={() => setConfirm(null)} onConfirm={() => { setConfirm(null); act.mutate('approve') }} />
      <ConfirmDialog open={confirm === 'dismiss'} title={p.confirmDismissTitle} text={p.confirmDismiss} confirmLabel={p.dismiss}
        onCancel={() => setConfirm(null)} onConfirm={() => { setConfirm(null); act.mutate('dismiss') }} />
    </>
  )
}
