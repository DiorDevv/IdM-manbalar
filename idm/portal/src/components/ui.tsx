import { useEffect, useRef, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { t } from '../i18n'
import { Icon } from './Icon'
import type { AccountStatus, AuditItem, ConnectorRun, EntitlementRef, IdentityStatus, RiskLevel, RunStatus } from '../api/types'
import { describeAction, formatDateTime } from '../i18n'

export function PageHead({ title, subtitle, actions, crumbs }: { title: ReactNode; subtitle?: ReactNode; actions?: ReactNode; crumbs?: ReactNode }) {
  return (
    <div>
      {crumbs && <div className="crumbs">{crumbs}</div>}
      <div className="page-head">
        <div><h1>{title}</h1>{subtitle && <p>{subtitle}</p>}</div>
        {actions && <div className="actions">{actions}</div>}
      </div>
    </div>
  )
}

export function Empty({ icon = 'people', title, text }: { icon?: string; title: string; text?: string }) {
  return (
    <div className="empty">
      <div className="icon"><Icon name={icon} size={20} /></div>
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

const runTone: Record<RunStatus, string> = { Running: 'info', Succeeded: 'ok', Failed: 'bad', Aborted: 'warn' }
export function RunStatusBadge({ status }: { status: RunStatus }) {
  return <span className={`badge ${runTone[status]}`}>{t.applications.runStatus[status]}</span>
}

/** Sinxronlash natijasi bir qatorda: holat, qachon, nima o'zgardi yoki xato matni. */
export function RunSummary({ run, hr = false }: { run: ConnectorRun | null; hr?: boolean }) {
  if (!run) return <span style={{ color: 'var(--muted)' }}>{t.applications.emptyRuns}</span>
  return (
    <div>
      <div style={{ display: 'flex', gap: 8, alignItems: 'center', flexWrap: 'wrap' }}>
        <RunStatusBadge status={run.status} />
        <span style={{ color: 'var(--muted)', fontSize: 13 }}>{t.ago(run.finishedAt ?? run.startedAt)}</span>
      </div>
      {run.status === 'Succeeded' && <div className="cell-sub">{hr ? t.applications.hrChanges(run) : t.applications.runChanges(run)}</div>}
      {run.error && <div className="run-error">{run.error}</div>}
    </div>
  )
}

export function GroupChips({ groups, max = 6 }: { groups: EntitlementRef[]; max?: number }) {
  if (groups.length === 0) return <span style={{ color: 'var(--faint)', fontSize: 13 }}>{t.identities.noGroups}</span>
  const shown = groups.slice(0, max)
  return (
    <div className="chips">
      {shown.map(g => (
        <span key={g.id} className={`chip${g.isPrivileged ? ' privileged' : ''}`} title={g.isPrivileged ? t.applications.privileged : undefined}>
          {g.isPrivileged && <Icon name="lock" size={12} />}{g.name}
        </span>
      ))}
      {groups.length > max && <span className="chip">+{groups.length - max}</span>}
    </div>
  )
}

export function Pager({ skip, page, total, onChange }: { skip: number; page: number; total: number; onChange: (skip: number) => void }) {
  if (total <= page) return null
  return (
    <div className="pager">
      <span>{t.identities.shown(skip + 1, Math.min(skip + page, total), total)}</span>
      <span style={{ display: 'flex', gap: 8 }}>
        <button className="btn secondary" disabled={skip === 0} onClick={() => onChange(Math.max(0, skip - page))}>{t.identities.prev}</button>
        <button className="btn secondary" disabled={skip + page >= total} onClick={() => onChange(skip + page)}>{t.identities.next}</button>
      </span>
    </div>
  )
}

/** Statistika kartasi. `to` bo'lsa butun karta havola; `attention` — faqat haqiqiy xavf (qiymat > 0) uchun. */
export type Tone = 'accent' | 'violet' | 'teal' | 'sky' | 'warn' | 'bad' | 'ok'

export function Stat({ icon, label, value, hint, attention, to, small, tone }: { icon?: string; label: string; value: ReactNode; hint?: ReactNode; attention?: boolean; to?: string; small?: boolean; tone?: Tone }) {
  const body = (
    <>
      <div className="label">{icon && <span className="chip-icon"><Icon name={icon} size={15} /></span>}{label}</div>
      <div className={`value${small ? ' small' : ''}`}>{value}</div>
      {hint && <div className="hint">{hint}</div>}
      {to && <span className="arrow"><Icon name="arrow" size={15} /></span>}
    </>
  )
  const className = `card stat${attention ? ' attention' : ''}`
  return to ? <Link to={to} className={className} data-tone={tone}>{body}</Link> : <div className={className} data-tone={tone}>{body}</div>
}

export interface TabDef<T extends string> { id: T; label: string; count?: number; bad?: boolean }
export function Tabs<T extends string>({ tabs, current, onSelect }: { tabs: TabDef<T>[]; current: T; onSelect: (id: T) => void }) {
  return (
    <div className="tabs" role="tablist">
      {tabs.map(x => (
        <button key={x.id} role="tab" aria-selected={current === x.id} className={`tab${current === x.id ? ' active' : ''}`} onClick={() => onSelect(x.id)}>
          {x.label}{x.count !== undefined && <span className={`count${x.bad && x.count > 0 ? ' bad' : ''}`}>{x.count}</span>}
        </button>
      ))}
    </div>
  )
}

/** Xavfli amalni tasdiqlash (brauzer confirm() o'rniga): nima bo'lishini aniq yozadi, Esc — bekor. */
export function ConfirmDialog({ open, title, text, confirmLabel, danger, onConfirm, onCancel }: {
  open: boolean; title: string; text: ReactNode; confirmLabel: string; danger?: boolean; onConfirm: () => void; onCancel: () => void
}) {
  const cancelRef = useRef<HTMLButtonElement>(null)
  useEffect(() => { if (open) cancelRef.current?.focus() }, [open])
  if (!open) return null
  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onCancel() }}
      onKeyDown={e => { if (e.key === 'Escape') onCancel() }}>
      <div className="dialog" role="alertdialog" aria-modal="true" aria-labelledby="confirm-title">
        <div className="dialog-body">
          <h2 id="confirm-title">{title}</h2>
          <p>{text}</p>
        </div>
        <div className="dialog-actions">
          <button ref={cancelRef} className="btn secondary" onClick={onCancel}>{t.cancel}</button>
          <button className={`btn ${danger ? 'danger' : 'primary'}`} onClick={onConfirm}>{confirmLabel}</button>
        </div>
      </div>
    </div>
  )
}

/** Ism bo'yicha doimiy rang: bir xodim har sahifada bir xil rangda. */
export function avatarClass(seed: string): string {
  let h = 0
  for (const ch of seed) h = (h * 31 + ch.charCodeAt(0)) >>> 0
  return `c${h % 8}`
}

export function Avatar({ name, size }: { name: string; size?: 'sm' }) {
  return <span className={`avatar ${avatarClass(name)}${size ? ` ${size}` : ''}`} aria-hidden="true">{initials(name)}</span>
}

/** Jadvaldagi xodim katagi: avatar, ism (havola) va ikkinchi qator. */
export function Person({ name, sub, to }: { name: string; sub?: ReactNode; to?: string }) {
  return (
    <div className="person">
      <Avatar name={name} size="sm" />
      <div>{to ? <Link to={to} className="cell-main" onClick={e => e.stopPropagation()}>{name}</Link> : <div className="cell-main">{name}</div>}
        {sub && <div className="cell-sub">{sub}</div>}</div>
    </div>
  )
}

/** Audit yozuvlari lentasi: har amal o'z ikonka va rangida, odam tilida. */
export function ActivityFeed({ items, showDetails = true }: { items: AuditItem[]; showDetails?: boolean }) {
  return (
    <div className="feed">
      {items.map(e => {
        const d = describeAction(e.action, e.details)
        return (
          <div className="feed-item" key={e.id} data-tone={d.tone}>
            <span className="feed-icon"><Icon name={d.icon} size={14} /></span>
            <div className="feed-text">
              {d.label} <span className="who">· {e.actorName ?? e.actorId}</span>
              {showDetails && d.summary && <div className="sub">{d.summary}</div>}
            </div>
            <time className="feed-time" dateTime={e.occurredAt} title={formatDateTime(e.occurredAt)}>{t.ago(e.occurredAt)}</time>
          </div>
        )
      })}
    </div>
  )
}
