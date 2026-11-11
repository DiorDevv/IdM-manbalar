import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { formatDate, t } from '../i18n'

/*
 * Grafiklar (dataviz qoidalari): tashqi kutubxona yo'q, ichki SVG. Ingichka belgilar (ustun ≤ 24px, tepasi 4px yumaloq, asosi to'g'ri),
 * yagona o'q, yordamchi chiziqlar ingichka va och. Ranglar tokenlardan (styles.css "Grafiklar"), matn — matn tokenlarida.
 * Har grafikda hover/fokus tooltip'i va klaviatura (←/→); qiymatlar jadval ko'rinishida ham bor (tooltip yagona yo'l emas).
 */

function useWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(0)
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const ro = new ResizeObserver(entries => setWidth(Math.floor(entries[0]!.contentRect.width)))
    ro.observe(el)
    return () => ro.disconnect()
  }, [])
  return [ref, width] as const
}

/** O'q uchun "toza" yuqori chegara: 1, 2, 5 × 10ⁿ. */
function niceMax(v: number): number {
  if (v <= 4) return 4
  const p = 10 ** Math.floor(Math.log10(v))
  const m = v / p
  return (m <= 1 ? 1 : m <= 2 ? 2 : m <= 5 ? 5 : 10) * p
}

export const compact = (n: number) => n >= 10_000 ? `${Math.round(n / 1000)}K` : n >= 1000 ? `${(n / 1000).toFixed(1).replace(/\.0$/, '')}K` : String(n)

/** Kichik trend chizig'i (stat kartasi uchun): kontekst — och kulrang, oxirgi nuqta — urg'u. Ikki nuqtadan kam bo'lsa chizilmaydi. */
export function Sparkline({ values, label }: { values: number[]; label: string }) {
  if (values.length < 2) return null
  const w = 96, h = 28, pad = 4
  const max = Math.max(...values), min = Math.min(...values)
  const span = max - min || 1
  const pts = values.map((v, i) => [pad + (i / (values.length - 1)) * (w - pad * 2), h - pad - ((v - min) / span) * (h - pad * 2)] as const)
  const d = pts.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)} ${y.toFixed(1)}`).join(' ')
  const [lx, ly] = pts.at(-1)!
  return (
    <svg className="spark" width={w} height={h} viewBox={`0 0 ${w} ${h}`} role="img" aria-label={label}>
      <path d={d} fill="none" stroke="var(--viz-muted)" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" />
      <circle cx={lx} cy={ly} r={4} fill="var(--viz-1)" stroke="var(--panel)" strokeWidth={2} />
    </svg>
  )
}

/** Bir qatorli kunlik ustunli grafik: kun bo'yicha son. Tooltip — kursor ostidagi kun (ustunga aniq tegish shart emas). */
export function ColumnChart({ days, values, label, unit }: { days: string[]; values: number[]; label: string; unit: (n: number) => string }) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const [active, setActive] = useState<number | null>(null)
  const height = 132, left = 30, right = 6, top = 10, bottom = 22
  const plotW = Math.max(0, width - left - right), plotH = height - top - bottom
  const max = niceMax(Math.max(0, ...values))
  const slot = values.length ? plotW / values.length : 0
  const barW = Math.max(1, Math.min(24, slot - 2))   // qo'shni ustunlar orasida kamida 2px bo'shliq
  const y = (v: number) => top + plotH - (v / max) * plotH
  const ticks = [0, max / 2, max]
  const xLabels = values.length > 1 ? [0, Math.floor((values.length - 1) / 2), values.length - 1] : [0]

  const onKey = (e: KeyboardEvent) => {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
    e.preventDefault()
    setActive(a => Math.max(0, Math.min(values.length - 1, (a ?? values.length - 1) + (e.key === 'ArrowLeft' ? -1 : 1))))
  }
  const bar = (v: number, i: number) => {
    const h = (v / max) * plotH
    if (h <= 0) return null
    const x = left + i * slot + (slot - barW) / 2, yt = top + plotH - h, r = Math.min(4, barW / 2, h)
    // Tepasi yumaloq, asosi to'g'ri
    return <path key={i} d={`M${x} ${top + plotH}V${yt + r}Q${x} ${yt} ${x + r} ${yt}H${x + barW - r}Q${x + barW} ${yt} ${x + barW} ${yt + r}V${top + plotH}Z`}
      fill="var(--viz-1)" opacity={active === null || active === i ? 1 : 0.55} />
  }
  const tip = active !== null && values[active] !== undefined ? { i: active, v: values[active]!, x: left + active * slot + slot / 2 } : null
  return (
    <div className="chart" ref={ref} tabIndex={0} role="img" aria-label={`${label}: ${values.reduce((a, b) => a + b, 0)}`}
      onKeyDown={onKey} onBlur={() => setActive(null)} onPointerLeave={() => setActive(null)}>
      {width > 0 && (
        <svg width={width} height={height} aria-hidden="true">
          {ticks.map(v => (
            <g key={v}>
              <line x1={left} x2={width - right} y1={y(v)} y2={y(v)} stroke="var(--viz-grid)" strokeWidth={1} />
              <text x={left - 6} y={y(v) + 4} textAnchor="end" className="axis">{compact(v)}</text>
            </g>
          ))}
          {tip && <line x1={tip.x} x2={tip.x} y1={top} y2={top + plotH} stroke="var(--viz-cross)" strokeWidth={1} />}
          {values.map(bar)}
          {xLabels.map((i, k) => (
            <text key={i} x={left + i * slot + slot / 2} y={height - 6} className="axis"
              textAnchor={k === 0 ? 'start' : k === xLabels.length - 1 ? 'end' : 'middle'}>{formatDate(days[i]).slice(0, 5)}</text>
          ))}
          {/* Tegish maydoni — butun kun ustuni balandligida */}
          {values.map((_, i) => (
            <rect key={i} x={left + i * slot} y={top} width={slot} height={plotH} fill="transparent" onPointerMove={() => setActive(i)} />
          ))}
        </svg>
      )}
      {tip && (
        <div className="chart-tip" style={{ left: Math.min(Math.max(tip.x, 60), width - 60) }} role="status">
          <b>{unit(tip.v)}</b><span>{formatDate(days[tip.i])}</span>
        </div>
      )}
    </div>
  )
}

export interface HeatCell { row: string; col: string; value: number; detail: string }

/** Issiqlik xaritasi: qator × ustun, bitta tus (ko'p — to'qroq), 0 — bo'sh. Katakda son yoziladi (rang yolg'iz emas). */
export function Heatmap({ rows, cols, cells, max, rowLabel, onSelect }: {
  rows: string[]; cols: string[]; cells: HeatCell[]; max: number; rowLabel: (r: string) => string; onSelect?: (c: HeatCell) => void
}) {
  const [active, setActive] = useState<HeatCell | null>(null)
  const map = new Map(cells.map(c => [`${c.row}\u0001${c.col}`, c]))
  const step = (v: number) => v <= 0 || max <= 0 ? 0 : Math.min(5, Math.max(1, Math.ceil((v / max) * 5)))
  return (
    <div className="heatmap" style={{ gridTemplateColumns: `minmax(120px, 200px) repeat(${cols.length}, minmax(56px, 1fr))` }}>
      <span />
      {cols.map(c => <span key={c} className="heat-col mono">{c}</span>)}
      {rows.map(r => (
        <div key={r} className="heat-row" role="row" style={{ display: 'contents' }}>
          <span className="heat-label" title={rowLabel(r)}>{rowLabel(r)}</span>
          {cols.map(c => {
            const cell = map.get(`${r}\u0001${c}`) ?? { row: r, col: c, value: 0, detail: '' }
            const s = step(cell.value)
            return (
              <button key={c} type="button" className={`heat-cell s${s}${active === cell ? ' active' : ''}`} disabled={cell.value === 0 && !onSelect}
                aria-label={`${rowLabel(r)}, ${c}: ${cell.value}${cell.detail ? ` (${cell.detail})` : ''}`}
                onPointerEnter={() => setActive(cell)} onPointerLeave={() => setActive(null)} onFocus={() => setActive(cell)} onBlur={() => setActive(null)}
                onClick={() => cell.value > 0 && onSelect?.(cell)}>
                {cell.value > 0 ? compact(cell.value) : ''}
              </button>
            )
          })}
        </div>
      ))}
      <div className="heat-foot" style={{ gridColumn: `1 / span ${cols.length + 1}` }}>
        <span className="heat-legend" aria-hidden="true">
          {t.dashboard.heatLess}{[1, 2, 3, 4, 5].map(s => <i key={s} className={`heat-cell s${s}`} />)}{t.dashboard.heatMore}
        </span>
        <span className="heat-readout" role="status">
          {active && active.value > 0 && <><b>{active.value}</b> {rowLabel(active.row)}, {active.col}{active.detail && `, ${active.detail}`}</>}
        </span>
      </div>
    </div>
  )
}
