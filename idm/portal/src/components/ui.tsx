import type { ReactNode } from 'react'
import { ApiError } from '../api/client'
import { t } from '../i18n'
import { Icon } from './Icon'
import type { AccountStatus, IdentityStatus, RiskLevel } from '../api/types'

export function PageHead({ title, subtitle, actions, crumbs }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; crumbs?: ReactNode }) {
  return (
    <div>
      {crumbs && <div className="crumbs">{crumbs}</div>}
      <div className="page-head">
        <div><h1>{title}</h1>{subtitle && <p>{subtitle}</p>}</div>
        {actions && <div>{actions}</div>}
      </div>
    </div>
  )
}

export function Empty({ icon = 'people', title, text }: { icon?: string; title: string; text?: string }) {
  return (
    <div className="empty">
      <div className="icon"><Icon name={icon} size={24} /></div>
      <h3>{title}</h3>
      {text && <p>{text}</p>}
    </div>
  )
}

export function ErrorBox({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  if (error instanceof ApiError && error.status === 403) return <Empty icon="shield" title={t.forbiddenTitle} text={t.forbiddenText} />
  return (
    <div className="card-body">
      <div className="alert bad"><Icon name="warning" size={18} /><span>{error instanceof Error ? error.message : String(error)}</span></div>
      {onRetry && <p><button className="btn secondary" onClick={onRetry}>{t.retry}</button></p>}
    </div>
  )
}

export function SkeletonRows({ rows = 5, cols = 5 }: { rows?: number; cols?: number }) {
  return (
    <tbody>
      {Array.from({ length: rows }, (_, r) => (
        <tr key={r}>{Array.from({ length: cols }, (_, c) => <td key={c}><div className="skeleton" style={{ width: `${50 + ((r + c) % 4) * 12}%` }} /></td>)}</tr>
      ))}
    </tbody>
  )
}

const statusTone: Record<string, string> = { Active: 'ok', Enabled: 'ok', Pending: 'warn', Suspended: 'warn', Disabled: 'neutral', Terminated: 'bad', Deleted: 'neutral' }
export function StatusBadge({ status }: { status: IdentityStatus | AccountStatus }) {
  return <span className={`badge ${statusTone[status] ?? 'neutral'}`}>{t.status[status]}</span>
}

const riskTone: Record<RiskLevel, string> = { Low: 'ok', Medium: 'info', High: 'warn', Critical: 'bad' }
export function RiskBadge({ risk }: { risk: RiskLevel }) {
  return <span className={`badge ${riskTone[risk]}`}>{t.risk[risk]}</span>
}

export function initials(name: string | null | undefined): string {
  const parts = (name ?? '?').trim().split(/\s+/).filter(Boolean)
  return ((parts[0]?.[0] ?? '?') + (parts[1]?.[0] ?? '')).toUpperCase()
}
