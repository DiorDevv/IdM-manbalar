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

/** O'zgarishlar JSON'i ({"department":{"from":..,"to":..}}) o'qiladigan qatorlarga. */
export function describeChanges(details: string | null): string[] {
  if (!details) return []
  try {
    const changes = JSON.parse(details) as Record<string, { from: unknown; to: unknown }>
    const show = (v: unknown) => (v === null || v === undefined || v === '' ? '—' : String(v))
    return Object.entries(changes).map(([field, c]) => `${field}: ${show(c.from)} → ${show(c.to)}`)
  } catch {
    return [details]
  }
}

type Tone = 'accent' | 'violet' | 'teal' | 'sky' | 'warn' | 'bad' | 'ok'
const actions: Record<string, [label: string, icon: string, tone: Tone]> = {
  'identity.created': ['Xodim qo\'shildi', 'user', 'ok'],
  'identity.updated': ['Xodim ma\'lumoti o\'zgardi', 'user', 'sky'],
  'account.discovered': ['Yangi akkaunt topildi', 'apps', 'teal'],
  'account.updated': ['Akkaunt o\'zgardi', 'apps', 'sky'],
  'account.disabled': ['Akkaunt bloklandi', 'lock', 'warn'],
  'account.enabled': ['Akkaunt qayta yoqildi', 'check', 'ok'],
  'account.restored': ['Akkaunt tiklandi', 'check', 'ok'],
  'account.deleted': ['Akkaunt o\'chirildi', 'x', 'bad'],
  'account.correlated': ['Akkaunt xodimga bog\'landi', 'reports', 'violet'],
  'account.entitlement-added': ['Guruhga qo\'shildi', 'people', 'violet'],
  'account.entitlement-removed': ['Guruhdan chiqarildi', 'people', 'warn'],
  'entitlement.discovered': ['Yangi guruh topildi', 'people', 'teal'],
  'entitlement.updated': ['Guruh o\'zgardi', 'people', 'sky'],
  'entitlement.removed': ['Guruh tizimda yo\'q', 'people', 'warn'],
  'entitlement.restored': ['Guruh qaytdi', 'people', 'ok'],
  'connector.initial-load': ['Birinchi yuklash', 'sync', 'ok'],
  'connector.sync-completed': ['Sinxronlash yakunlandi', 'sync', 'ok'],
  'connector.sync-failed': ['Sinxronlash xatosi', 'warning', 'bad'],
  'connector.sync-aborted': ['Sinxronlash to\'xtatildi', 'warning', 'warn'],
  'connector.mass-delete-approved': ['Ommaviy o\'chirish tasdiqlandi', 'warning', 'bad'],
  'application.registered': ['Tizim ulandi', 'apps', 'violet'],
  'portal-role.granted': ['Portal vakolati berildi', 'shield', 'violet'],
  'identity.role-assigned': ['Rol berildi', 'role', 'violet'],
  'identity.role-removed': ['Rol olib tashlandi', 'role', 'warn'],
  'role.created': ['Rol yaratildi', 'role', 'violet'],
  'role.updated': ['Rol tahrirlandi', 'role', 'sky'],
  'role.deleted': ['Rol o\'chirildi', 'role', 'bad'],
  'role.entitlements-changed': ['Rol ruxsatlari o\'zgardi', 'lock', 'sky'],
  'role.rule-added': ['Avtomatik qoida qo\'shildi', 'sparkle', 'teal'],
  'role.rule-removed': ['Avtomatik qoida olib tashlandi', 'sparkle', 'warn'],
  'sod.rule-created': ['SoD qoidasi yaratildi', 'shield', 'violet'],
  'sod.rule-deleted': ['SoD qoidasi o\'chirildi', 'shield', 'bad'],
  'request.created': ['Ariza yuborildi', 'request', 'sky'],
  'request.step-approved': ['Ariza bosqichi tasdiqlandi', 'check', 'ok'],
  'request.approved': ['Ariza tasdiqlandi', 'check', 'ok'],
  'request.rejected': ['Ariza rad etildi', 'x', 'bad'],
  'request.cancelled': ['Ariza bekor qilindi', 'x', 'warn'],
  'request.fulfilled': ['Ariza bajarildi', 'check', 'ok'],
  'review.campaign-created': ['Attestatsiya boshlandi', 'review', 'violet'],
  'review.campaign-completed': ['Attestatsiya yakunlandi', 'review', 'ok'],
  'review.campaign-cancelled': ['Attestatsiya bekor qilindi', 'review', 'warn'],
  'review.kept': ['Attestatsiya: ruxsat qoldirildi', 'check', 'ok'],
  'review.revoked': ['Attestatsiya: ruxsat olib tashlandi', 'x', 'bad'],
  'review.remediated': ['Attestatsiya: tizimdan olindi', 'check', 'teal'],
  'report.exported': ['Hisobot yuklab olindi', 'reports', 'sky'],
  'provisioning.planned': ['Tizimga yozish rejalandi', 'sync', 'sky'],
  'provisioning.held': ['Ko\'p amal: tasdiqqa to\'xtatildi', 'warning', 'warn'],
  'provisioning.approved': ['Tizimga yozish tasdiqlandi', 'check', 'violet'],
  'provisioning.retried': ['Qayta urinish buyurildi', 'sync', 'sky'],
  'provisioning.dismissed': ['Xato amal yopildi', 'x', 'warn'],
  'provisioning.account-created': ['Tizimda akkaunt yaratildi', 'user', 'ok'],
  'provisioning.account-disabled': ['Tizimda akkaunt bloklandi', 'lock', 'warn'],
  'provisioning.membership-added': ['Tizimda guruhga qo\'shildi', 'people', 'violet'],
  'provisioning.membership-removed': ['Tizimda guruhdan chiqarildi', 'people', 'warn'],
  'provisioning.failed': ['Tizimga yozib bo\'lmadi', 'warning', 'bad'],
  'provisioning.obsolete': ['Amal keraksiz bo\'lib qoldi', 'x', 'accent'],
}

/** Audit amali → odam tilidagi nom, ikonka, rang va tafsilotlardan qisqa xulosa (login, tizim, guruh...). */
export function describeAction(action: string, details: string | null): { label: string; icon: string; tone: Tone; summary: string } {
  const [label, icon, tone] = actions[action] ?? [action, 'audit', 'accent' as Tone]
  let summary = ''
  if (details) {
    try {
      const d = lowerKeys(JSON.parse(details) as Record<string, unknown>)
      const parts = ['application', 'login', 'employeenumber', 'beneficiary', 'group', 'code', 'name', 'role', 'step', 'department', 'jobtitle', 'comment', 'error']
        .map(k => d[k]).filter((v): v is string | number => typeof v === 'string' || typeof v === 'number')
      if (typeof d.accountsread === 'number') parts.push(`${d.accountsread} akkaunt`)
      if (typeof d.records === 'number') parts.push(`${d.records} yozuv`)
      const changes = syncChanges(d)
      if (changes > 0) parts.push(`${changes} o'zgarish`)
      summary = parts.map(String).join(' · ')
    } catch { /* matn bo'lsa xulosa yo'q */ }
  }
  return { label, icon, tone, summary }
}

function lowerKeys(d: Record<string, unknown>): Record<string, unknown> {
  return Object.fromEntries(Object.entries(d).map(([k, v]) => [k.toLowerCase(), v]))
}

function syncChanges(d: Record<string, unknown>): number {
  return Object.entries(d).filter(([k, v]) => typeof v === 'number' && /(created|updated|disabled|deleted|added|removed|terminated|missing)$/.test(k))
    .reduce((sum, [, v]) => sum + (v as number), 0)
}

/** Jadval bo'yicha o'zgarishsiz sinxronlash — lentada shovqin (auditda baribir saqlanadi). */
export function isQuietSync(action: string, details: string | null): boolean {
  if (action !== 'connector.sync-completed' || !details) return false
  try {
    const d = lowerKeys(JSON.parse(details) as Record<string, unknown>)
    return syncChanges(d) === 0 && !d.warnings
  } catch { return false }
}
