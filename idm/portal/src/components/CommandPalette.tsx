import { useDeferredValue, useEffect, useMemo, useRef, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api, query } from '../api/client'
import type { IdentityListItem, PagedResult } from '../api/types'
import { t } from '../i18n'
import { Icon } from './Icon'
import { NAV } from './nav'

interface Item { key: string; group: string; icon: string; label: string; meta?: string; to: string }

/** ⌘K / Ctrl+K: sahifaga o'tish va xodimni ism yoki tabel raqami bo'yicha topish. */
export function CommandPalette({ open, onClose, canSearch }: { open: boolean; onClose: () => void; canSearch: boolean }) {
  // canSearch — administrator/auditor: xodim qidirish va barcha sahifalar; oddiy xodim — faqat o'ziga ochiq sahifalar
  const navigate = useNavigate()
  const [text, setText] = useState('')
  const [active, setActive] = useState(0)
  const input = useRef<HTMLInputElement>(null)
  const deferred = useDeferredValue(text.trim())

  const people = useQuery({
    queryKey: ['cmd-identities', deferred],
    queryFn: () => api<PagedResult<IdentityListItem>>(`/api/identities${query({ search: deferred, take: 6 })}`),
    enabled: open && canSearch && deferred.length >= 2,
    staleTime: 30_000,
  })

  const items = useMemo<Item[]>(() => {
    const q = text.trim().toLowerCase()
    const pages = NAV.flatMap(s => s.items).filter(i => !i.soon && (canSearch || i.everyone) && (!q || i.label.toLowerCase().includes(q)))
      .map(i => ({ key: i.to, group: t.cmd.pages, icon: i.icon, label: i.label, to: i.to }))
    const found = (deferred.length >= 2 ? people.data?.items ?? [] : []).map(p => ({
      key: p.id, group: t.cmd.people, icon: 'user', label: p.displayName, meta: p.employeeNumber, to: `/identities/${p.id}`,
    }))
    return [...found, ...pages]
  }, [text, deferred, people.data, canSearch])

  useEffect(() => { if (open) { setText(''); setActive(0); setTimeout(() => input.current?.focus(), 0) } }, [open])
  useEffect(() => { setActive(0) }, [text])

  if (!open) return null
  const go = (item: Item | undefined) => { if (item) { onClose(); navigate(item.to) } }
  const onKey = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setActive(a => Math.min(a + 1, items.length - 1)) }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(a => Math.max(a - 1, 0)) }
    else if (e.key === 'Enter') { e.preventDefault(); go(items[active]) }
    else if (e.key === 'Escape') { e.preventDefault(); onClose() }
  }

  let lastGroup = ''
  return (
    <div className="overlay" onMouseDown={e => { if (e.target === e.currentTarget) onClose() }}>
      <div className="dialog cmd" role="dialog" aria-modal="true" aria-label={t.cmd.title} onKeyDown={onKey}>
        <div className="cmd-input">
          <Icon name="search" />
          <input ref={input} value={text} onChange={e => setText(e.target.value)} placeholder={canSearch ? t.cmd.placeholder : t.cmd.placeholderPages}
            role="combobox" aria-expanded="true" aria-controls="cmd-list" aria-activedescendant={items[active] ? `cmd-${items[active].key}` : undefined} />
          {people.isFetching && <span className="spinner" aria-hidden="true" />}
          <span className="kbd">Esc</span>
        </div>
        <div className="cmd-list" id="cmd-list" role="listbox">
          {items.length === 0 && <div className="empty" style={{ padding: 28 }}><p>{t.cmd.nothing}</p></div>}
          {items.map((item, index) => {
            const header = item.group !== lastGroup ? <div className="cmd-group" key={`g-${item.group}`}>{item.group}</div> : null
            lastGroup = item.group
            return [header,
              <button key={item.key} id={`cmd-${item.key}`} role="option" aria-selected={index === active} className="cmd-item"
                onMouseMove={() => setActive(index)} onClick={() => go(item)}>
                <Icon name={item.icon} size={16} />{item.label}{item.meta && <span className="meta mono">{item.meta}</span>}
              </button>]
          })}
        </div>
        <div className="cmd-foot">
          <span><span className="kbd">↑</span><span className="kbd">↓</span>{t.cmd.move}</span>
          <span><span className="kbd">↵</span>{t.cmd.open}</span>
        </div>
      </div>
    </div>
  )
}
