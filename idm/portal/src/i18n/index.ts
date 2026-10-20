import { uz } from './uz'

// Hozircha bitta til; tarjimalar qo'shilganda shu yerda tanlanadi.
export const t = uz

// O'zbekistonda odatiy format: 20.10.2026 17:38. Brauzerlarning "uz" ICU ma'lumotlari to'liq emas ("2026 M10 20").
const pad = (n: number) => String(n).padStart(2, '0')

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '—' : `${pad(d.getDate())}.${pad(d.getMonth() + 1)}.${d.getFullYear()}`
}

export function formatDateTime(value: string | null | undefined): string {
  if (!value) return '—'
  const d = new Date(value)
  return Number.isNaN(d.getTime()) ? '—' : `${formatDate(value)} ${pad(d.getHours())}:${pad(d.getMinutes())}`
}
