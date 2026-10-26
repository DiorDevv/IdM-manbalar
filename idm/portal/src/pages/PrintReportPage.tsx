import { useQuery } from '@tanstack/react-query'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api, query } from '../api/client'
import { useMe } from '../api/me'
import type { ReportTable } from '../api/types'
import { formatDateTime, t } from '../i18n'
import { Icon } from '../components/Icon'
import { Empty, ErrorBox } from '../components/ui'

const x = t.exports

/** Hisobotning chop etish ko'rinishi: brauzerning "Chop etish → PDF sifatida saqlash" bilan PDF olinadi (menyu va tugmalarsiz, A4 albom). */
export function PrintReportPage() {
  const { key = '' } = useParams()
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const me = useMe()
  const campaignId = params.get('campaignId') ?? undefined
  const report = useQuery({
    queryKey: ['report-print', key, campaignId],
    queryFn: () => api<ReportTable>(`/api/reports/export/${encodeURIComponent(key)}${query({ campaignId })}`),
    staleTime: Infinity,
  })
  if (report.isError) return <div className="card"><ErrorBox error={report.error} /></div>
  if (!report.data) return <div className="muted">{t.loading}</div>
  const r = report.data
  return (
    <>
      <div className="toolbar no-print" style={{ border: 0, padding: '0 0 16px' }}>
        <button className="btn ghost sm" onClick={() => navigate(-1)}><Icon name="chevron" size={15} />{x.back}</button>
        <button className="btn primary sm" style={{ marginLeft: 'auto' }} onClick={() => window.print()}><Icon name="audit" size={15} />{x.print}</button>
      </div>
      <section className="card">
        <div className="card-body">
          <div className="muted" style={{ fontSize: 12 }}>Bank IdM</div>
          <h1 style={{ margin: '4px 0', fontSize: 20 }}>{r.title}</h1>
          {r.subtitle && <div className="muted">{r.subtitle}</div>}
          <div className="muted" style={{ fontSize: 12, marginTop: 6 }}>
            {x.generated}: {formatDateTime(r.generatedAt)} · {x.by}: {me.data?.name ?? me.data?.login} · {x.rows(r.rows.length)}
          </div>
        </div>
        {r.rows.length === 0 ? <Empty icon="reports" title={x.empty} /> : (
          <div className="table-wrap">
            <table className="data">
              <thead><tr>{r.columns.map(c => <th key={c}>{c}</th>)}</tr></thead>
              <tbody>{r.rows.map((row, i) => <tr key={i}>{r.columns.map((_, c) => <td key={c}>{row[c] ?? ''}</td>)}</tr>)}</tbody>
            </table>
          </div>
        )}
      </section>
    </>
  )
}
