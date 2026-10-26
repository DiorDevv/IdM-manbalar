import { useState } from 'react'
import { Link } from 'react-router-dom'
import { download, query } from '../api/client'
import type { ReportKey } from '../api/types'
import { t } from '../i18n'
import { Icon } from './Icon'

/** Hisobotni Excel sifatida yuklab olish yoki chop etish (PDF) ko'rinishini ochish. */
export function ExportButtons({ report, campaignId }: { report: ReportKey; campaignId?: string }) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const excel = async () => {
    setBusy(true)
    setError(null)
    try {
      await download(`/api/reports/export/${report}${query({ format: 'xlsx', campaignId })}`, `idm-${report}.xlsx`)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }
  return (
    <span className="no-print" style={{ display: 'inline-flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
      <button className="btn secondary sm" disabled={busy} onClick={() => { void excel() }}><Icon name="reports" size={15} />{busy ? t.exports.downloading : t.exports.excel}</button>
      <Link className="btn secondary sm" to={`/reports/print/${report}${query({ campaignId })}`}><Icon name="audit" size={15} />{t.exports.pdf}</Link>
      {error && <span style={{ color: 'var(--bad)', fontSize: 12.5 }} role="alert">{error}</span>}
    </span>
  )
}
