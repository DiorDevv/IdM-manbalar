import { useSyncExternalStore } from 'react'

/** Foydalanuvchi tanlovi: och, tungi yoki tizim sozlamasiga ergashish. Brauzerda (localStorage) saqlanadi. */
export type ThemeChoice = 'light' | 'dark' | 'system'

const KEY = 'idm.theme'
const media = typeof window !== 'undefined' ? window.matchMedia('(prefers-color-scheme: dark)') : null
const listeners = new Set<() => void>()

function read(): ThemeChoice {
  try {
    const v = localStorage.getItem(KEY)
    return v === 'light' || v === 'dark' ? v : 'system'
  } catch {
    return 'system'
  }
}

let choice: ThemeChoice = read()

function apply() {
  const dark = choice === 'dark' || (choice === 'system' && !!media?.matches)
  document.documentElement.dataset.theme = dark ? 'dark' : 'light'
}

/** main.tsx'da render'dan oldin chaqiriladi: sahifa noto'g'ri mavzuda "yonib" ketmaydi. */
export function initTheme() {
  apply()
  media?.addEventListener('change', () => { if (choice === 'system') { apply(); listeners.forEach(l => l()) } })
}

export function setTheme(next: ThemeChoice) {
  choice = next
  try { if (next === 'system') localStorage.removeItem(KEY); else localStorage.setItem(KEY, next) } catch { /* saqlab bo'lmasa ham ishlaydi */ }
  apply()
  listeners.forEach(l => l())
}

/** Keyingi tanlov: tizim → och → tungi → tizim. */
export function nextTheme(current: ThemeChoice): ThemeChoice {
  return current === 'system' ? 'light' : current === 'light' ? 'dark' : 'system'
}

export function useTheme(): ThemeChoice {
  return useSyncExternalStore(l => { listeners.add(l); return () => listeners.delete(l) }, () => choice, () => 'system')
}
