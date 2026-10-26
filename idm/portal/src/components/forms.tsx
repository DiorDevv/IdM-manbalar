import { useDeferredValue, useEffect, useRef, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api, ApiError, query } from '../api/client'
import type { EntitlementPick, RiskLevel } from '../api/types'
import { t } from '../i18n'
import { Icon } from './Icon'

/** Modal oyna: Esc va fon bosilganda yopiladi, ochilganda birinchi maydonga fokus. */
export function Modal({ open, title, onClose, children, footer, wide }: {
  open: boolean; title: string; onClose: () => void; children: ReactNode; footer?: ReactNode; wide?: boolean
}) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    // Avval forma maydoni (sarlavhadagi "×" emas), u bo'lmasa birinchi tugma
    setTimeout(() => (ref.current?.querySelector<HTMLElement>('.dialog-body input, .dialog-body select, .dialog-body textarea')
      ?? ref.current?.querySelector<HTMLElement>('.dialog-actions button'))?.focus(), 0)
  }, [open])
  if (!open) return null
  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onClose() }} onKeyDown={e => { if (e.key === 'Escape') onClose() }}>
      <div ref={ref} className={`dialog${wide ? ' wide' : ''}`} role="dialog" aria-modal="true" aria-label={title}>
        <div className="dialog-head"><h2>{title}</h2><button className="icon-btn" onClick={onClose} aria-label={t.cancel}><Icon name="x" size={16} /></button></div>
        <div className="dialog-body">{children}</div>
        {footer && <div className="dialog-actions">{footer}</div>}
      </div>
    </div>
  )
}

export function Field({ label, error, help, children }: { label: string; error?: string; help?: string; children: ReactNode }) {
  return (
    <div className="field">
      <label>{label}{children}</label>
      {error ? <span className="error" role="alert">{error}</span> : help && <span className="help">{help}</span>}
    </div>
  )
}

/** Server validatsiya xatosidan maydon xabari (masalan "code" → "Kod: 2–64 belgi..."). */
export function fieldError(error: unknown, field: string): string | undefined {
  return error instanceof ApiError ? error.fieldErrors[field]?.[0] : undefined
}

/** Umumiy xato (maydonga tegishli bo'lmagan: 409, 500, tarmoq). */
export function formError(error: unknown, fields: string[]): string | undefined {
  if (!error) return undefined
  if (error instanceof ApiError && fields.some(f => error.fieldErrors[f])) return undefined
  return error instanceof Error ? error.message : String(error)
}

export const risks: RiskLevel[] = ['Low', 'Medium', 'High', 'Critical']

/** Ruxsatlarni (AD guruhlari va h.k.) qidirib belgilash. Imtiyozlilar qulf belgisi bilan. */
export function EntitlementPicker({ selected, onChange, exclude = [] }: { selected: EntitlementPick[]; onChange: (next: EntitlementPick[]) => void; exclude?: string[] }) {
  const [search, setSearch] = useState('')
  const deferred = useDeferredValue(search.trim())
  const list = useQuery({
    queryKey: ['entitlement-pick', deferred],
    queryFn: () => api<EntitlementPick[]>(`/api/entitlements${query({ search: deferred, take: 100 })}`),
    staleTime: 30_000,
  })
  const chosen = new Set(selected.map(s => s.id))
  const toggle = (e: EntitlementPick) => onChange(chosen.has(e.id) ? selected.filter(s => s.id !== e.id) : [...selected, e])
  const items = (list.data ?? []).filter(e => !exclude.includes(e.id))
  return (
    <div className="picker">
      <label className="search"><Icon name="search" size={15} />
        <input className="input" placeholder={t.roles.searchEntitlements} value={search} onChange={e => setSearch(e.target.value)} aria-label={t.roles.searchEntitlements} />
      </label>
      <div className="picker-list" role="listbox" aria-multiselectable="true">
        {list.isPending && <div className="muted" style={{ padding: 10, fontSize: 13 }}>{t.loading}</div>}
        {list.data && items.length === 0 && <div className="muted" style={{ padding: 10, fontSize: 13 }}>{t.cmd.nothing}</div>}
        {items.map(e => (
          <label key={e.id} className="picker-item" role="option" aria-selected={chosen.has(e.id)}>
            <input type="checkbox" checked={chosen.has(e.id)} onChange={() => toggle(e)} />
            {e.isPrivileged && <span style={{ color: 'var(--warn)' }}><Icon name="lock" size={13} /></span>}
            <span className="name" title={e.name}>{e.name}</span><span className="app-code">{e.applicationCode}</span>
          </label>
        ))}
      </div>
    </div>
  )
}
