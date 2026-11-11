import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { DayValue, RiskMatrixResponse, TrendsResponse } from '../api/types'
import { t } from '../i18n'
import { ColumnChart, Heatmap, Sparkline, type HeatCell } from '../components/charts'
import { Empty, ErrorBox } from '../components/ui'
import { Icon } from '../components/Icon'

const dash = t.dashboard
type Range = 7 | 30 | 90
const OTHER = '\u0000other'

export const useTrends = (days: Range) => useQuery({
  queryKey: ['trends', days], queryFn: () => api<TrendsResponse>(`/api/dashboard/trends?days=${days}`),
  placeholderData: keepPreviousData, staleTime: 60_000,
})

/** Holat ko'rsatkichining 7 kun oldingiga nisbatan o'zgarishi (bu ko'rsatkichlarning hammasida o'sish — yomon). */
export function TrendFooter({ series, label }: { series?: DayValue[]; label: string }) {
  if (!series || series.length < 2) return null
  const last = series.at(-1)!
  const weekAgo = new Date(last.day); weekAgo.setDate(weekAgo.getDate() - 7)
  const base = [...series].reverse().find(p => new Date(p.day) <= weekAgo) ?? series[0]!
  const d = Math.round(last.value - base.value)
  return (
    <>
      <span className={`delta ${d > 0 ? 'bad' : d < 0 ? 'good' : 'flat'}`}>{dash.deltaWeek(d)}</span>
      <Sparkline values={series.slice(-14).map(p => p.value)} label={dash.trendOf(label)} />
    </>
  )
}

/** Bosh sahifadagi nazorat markazi: davr filtri → kunlik faollik (kichik grafiklar) → xavf xaritasi va holat dinamikasi. */
export function ControlCenter() {
  const [range, setRange] = useState<Range>(30)
  const trends = useTrends(range)
  const d = trends.data
  const sum = (xs: number[] | undefined) => (xs ?? []).reduce((a, b) => a + b, 0)
  return (
    <>
      <div className="control-head">
        <div><h2>{dash.controlTitle}</h2><p>{dash.controlSubtitle}</p></div>
        <div className="segmented" role="group" aria-label={dash.rangeLabel}>
          {([7, 30, 90] as const).map(n => (
            <button key={n} type="button" aria-pressed={range === n} onClick={() => setRange(n)}>{dash.range(n)}</button>
          ))}
        </div>
      </div>
      {trends.isError ? <section className="card" style={{ marginBottom: 14 }}><ErrorBox error={trends.error} onRetry={() => { void trends.refetch() }} /></section> : (
        <div className={`grid multiples${trends.isPlaceholderData ? ' stale' : ''}`}>
          <FlowCard title={dash.chartRequests} sub={dash.chartRequestsSub} days={d?.days} values={d?.flows.requests} unit={dash.unitRequests} to="/requests?tab=all" />
          <FlowCard title={dash.chartJml} sub={dash.chartJmlSub} days={d?.days} values={d?.flows.jml} unit={dash.unitEvents} to="/events" />
          <FlowCard title={dash.chartProvisioning} sub={dash.chartProvisioningSub} days={d?.days} values={d?.flows.provisioned} unit={dash.unitOps} to="/provisioning"
            note={sum(d?.flows.provisioningFailed) > 0 ? dash.provisioningFailed(sum(d?.flows.provisioningFailed)) : undefined} />
        </div>
      )}
      <div className="grid two-col" style={{ marginBottom: 14 }}>
        <RiskMap />
        <Levels levels={d?.levels} />
      </div>
    </>
  )
}

function FlowCard({ title, sub, days, values, unit, to, note }: {
  title: string; sub: string; days?: string[]; values?: number[]; unit: (n: number) => string; to: string; note?: string
}) {
  const total = (values ?? []).reduce((a, b) => a + b, 0)
  return (
    <section className="card chart-card">
      <div className="card-head">
        <div>
          <h2>{title}</h2>
          <div className="total num">{values ? total : '…'}{note && <small style={{ color: 'var(--bad)' }}>{note}</small>}</div>
          <div className="sub">{sub}</div>
        </div>
        <Link to={to} aria-label={title}><Icon name="chevron" size={14} /></Link>
      </div>
      {days && values ? <ColumnChart days={days} values={values} label={title} unit={unit} /> : <div className="card-body"><div className="skeleton" style={{ height: 120 }} /></div>}
    </section>
  )
}

function RiskMap() {
  const navigate = useNavigate()
  const [table, setTable] = useState(false)
  const risk = useQuery({ queryKey: ['risk-matrix'], queryFn: () => api<RiskMatrixResponse>('/api/dashboard/risk-matrix'), staleTime: 60_000 })
  const label = (r: string) => r === OTHER ? dash.otherDepartments : r || dash.noDepartment
  const cells: HeatCell[] = (risk.data?.cells ?? []).map(c => ({ row: c.department, col: c.application, value: c.excess, detail: dash.heatDetail(c.identities, c.privileged) }))
  return (
    <section className="card">
      <div className="card-head">
        <div><h2>{dash.heatTitle}</h2><div className="sub" style={{ fontSize: 12, color: 'var(--muted)', marginTop: 2 }}>{dash.heatSub}</div></div>
        {risk.data && risk.data.cells.length > 0 && (
          <div className="segmented">
            <button type="button" aria-pressed={!table} onClick={() => setTable(false)}>{dash.chartView}</button>
            <button type="button" aria-pressed={table} onClick={() => setTable(true)}>{dash.tableView}</button>
          </div>
        )}
      </div>
      {risk.isError ? <ErrorBox error={risk.error} onRetry={() => { void risk.refetch() }} />
        : !risk.data ? <div className="card-body"><div className="skeleton" style={{ height: 160 }} /></div>
        : risk.data.cells.length === 0 ? <Empty icon="shield" title={dash.heatEmpty} text={dash.heatEmptyText} />
        : table ? (
          <div className="table-wrap">
            <table className="data">
              <thead><tr><th>{dash.colDepartment}</th><th>{dash.colSystem}</th><th className="num" style={{ textAlign: 'right' }}>{dash.colExcess}</th>
                <th className="num" style={{ textAlign: 'right' }}>{dash.colPrivileged}</th><th className="num" style={{ textAlign: 'right' }}>{dash.colPeople}</th></tr></thead>
              <tbody>
                {[...risk.data.cells].sort((a, b) => b.excess - a.excess).map(c => (
                  <tr key={`${c.department}-${c.application}`}>
                    <td>{label(c.department)}</td><td className="mono">{c.application}</td>
                    <td className="num" style={{ textAlign: 'right' }}>{c.excess}</td><td className="num" style={{ textAlign: 'right' }}>{c.privileged}</td>
                    <td className="num" style={{ textAlign: 'right' }}>{c.identities}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <Heatmap rows={risk.data.departments} cols={risk.data.applications} cells={cells} max={risk.data.maxExcess} rowLabel={label}
            onSelect={() => navigate('/reports?tab=drift')} />
        )}
    </section>
  )
}

const LEVELS = [
  { key: 'excess_access', to: '/reports?tab=drift' },
  { key: 'sod_open', to: '/sod' },
  { key: 'pending_requests', to: '/requests?tab=all' },
  { key: 'orphan_accounts', to: '/reports?tab=orphans' },
  { key: 'leavers_active', to: '/reports?tab=leavers' },
]

function Levels({ levels }: { levels?: Record<string, DayValue[]> }) {
  const collecting = !levels || Object.values(levels).every(s => s.length < 2)
  return (
    <section className="card">
      <div className="card-head"><h2>{dash.levelsTitle}</h2></div>
      {LEVELS.map(({ key, to }) => {
        const series = levels?.[key]
        return (
          <Link key={key} to={to} className="level-row">
            <span>{dash.level[key]}</span>
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: 10 }}><TrendFooter series={series} label={dash.level[key] ?? key} /></span>
            <b>{series?.at(-1)?.value ?? '—'}</b>
          </Link>
        )
      })}
      {collecting && <div className="card-body muted" style={{ fontSize: 12.5, borderTop: '1px solid var(--border)' }}>{dash.levelsCollecting}</div>}
    </section>
  )
}
