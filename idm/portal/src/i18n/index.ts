import { uz } from './uz'
import { ru } from './ru'
import type { Dictionary, Language } from './types'

export type { Language } from './types'
export const languages: Language[] = ['uz', 'ru']
const KEY = 'idm.lang'

function readLanguage(): Language {
  try {
    const saved = localStorage.getItem(KEY)
    return saved === 'ru' || saved === 'uz' ? saved : 'uz'
  } catch { return 'uz' }
}

/** Tanlangan til (standart — o'zbekcha). O'zgartirish sahifani qayta yuklaydi: matnlar modul darajasida o'qiladi. */
export const language: Language = readLanguage()
export const t: Dictionary = language === 'ru' ? ru : uz
if (typeof document !== 'undefined') document.documentElement.lang = language

export function setLanguage(next: Language) {
  try { localStorage.setItem(KEY, next) } catch { /* saqlab bo'lmasa — til o'zgarmaydi */ }
  location.reload()
}

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
  'role.certified': ['Rol tarkibi tasdiqlandi', 'review', 'ok'],
  'sod.rule-created': ['SoD qoidasi yaratildi', 'shield', 'violet'],
  'sod.rule-deleted': ['SoD qoidasi o\'chirildi', 'shield', 'bad'],
  'delegation.created': ['O\'rinbosar tayinlandi', 'people', 'sky'],
  'delegation.revoked': ['O\'rinbosarlik bekor qilindi', 'people', 'accent'],
  'sod.exception-granted': ['SoD istisnosi berildi', 'shield', 'warn'],
  'sod.exception-revoked': ['SoD istisnosi bekor qilindi', 'shield', 'accent'],
  'account.entitlement-changed': ['Guruh a\'zoligi turi o\'zgardi', 'people', 'sky'],
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
  'provisioning.account-enabled': ['Tizimda akkaunt qayta yoqildi', 'unlock', 'ok'],
  'provisioning.membership-added': ['Tizimda guruhga qo\'shildi', 'people', 'violet'],
  'provisioning.membership-removed': ['Tizimda guruhdan chiqarildi', 'people', 'warn'],
  'provisioning.failed': ['Tizimga yozib bo\'lmadi', 'warning', 'bad'],
  'provisioning.obsolete': ['Amal keraksiz bo\'lib qoldi', 'x', 'accent'],
}

/** Audit amallari ruscha (o'zbekchasi va ikonkasi — yuqorida). */
const actionsRu: Record<string, string> = {
  'identity.created': 'Сотрудник добавлен', 'identity.updated': 'Данные сотрудника изменены',
  'account.discovered': 'Найдена новая учётная запись', 'account.updated': 'Учётная запись изменена', 'account.disabled': 'Учётная запись заблокирована',
  'account.enabled': 'Учётная запись снова включена', 'account.restored': 'Учётная запись восстановлена', 'account.deleted': 'Учётная запись удалена',
  'account.correlated': 'Учётная запись привязана к сотруднику', 'account.entitlement-added': 'Добавлен в группу', 'account.entitlement-removed': 'Исключён из группы',
  'account.entitlement-changed': 'Изменился тип членства в группе',
  'entitlement.discovered': 'Найдена новая группа', 'entitlement.updated': 'Группа изменена', 'entitlement.removed': 'Группы нет в системе', 'entitlement.restored': 'Группа вернулась',
  'connector.initial-load': 'Первая загрузка', 'connector.sync-completed': 'Синхронизация завершена', 'connector.sync-failed': 'Ошибка синхронизации',
  'connector.sync-aborted': 'Синхронизация остановлена', 'connector.mass-delete-approved': 'Массовое удаление подтверждено',
  'application.registered': 'Система подключена', 'portal-role.granted': 'Выдано полномочие портала',
  'identity.role-assigned': 'Роль выдана', 'identity.role-removed': 'Роль отозвана',
  'role.created': 'Роль создана', 'role.updated': 'Роль изменена', 'role.deleted': 'Роль удалена', 'role.entitlements-changed': 'Права роли изменены',
  'role.rule-added': 'Добавлено автоматическое правило', 'role.rule-removed': 'Автоматическое правило удалено', 'role.certified': 'Состав роли подтверждён',
  'sod.rule-created': 'Создано правило SoD', 'sod.rule-deleted': 'Правило SoD удалено',
  'sod.exception-granted': 'Выдано исключение SoD', 'sod.exception-revoked': 'Исключение SoD отменено',
  'delegation.created': 'Назначен заместитель', 'delegation.revoked': 'Замещение отменено',
  'request.created': 'Заявка отправлена', 'request.step-approved': 'Этап заявки согласован', 'request.approved': 'Заявка согласована',
  'request.rejected': 'Заявка отклонена', 'request.cancelled': 'Заявка отменена', 'request.fulfilled': 'Заявка исполнена',
  'review.campaign-created': 'Аттестация начата', 'review.campaign-completed': 'Аттестация завершена', 'review.campaign-cancelled': 'Аттестация отменена',
  'review.kept': 'Аттестация: право оставлено', 'review.revoked': 'Аттестация: право отозвано', 'review.remediated': 'Аттестация: удалено из системы',
  'report.exported': 'Отчёт выгружен',
  'provisioning.planned': 'Запись в системы запланирована', 'provisioning.held': 'Много действий: остановлено до подтверждения',
  'provisioning.approved': 'Запись в системы подтверждена', 'provisioning.retried': 'Назначена повторная попытка', 'provisioning.dismissed': 'Ошибочное действие закрыто',
  'provisioning.account-created': 'В системе создана учётная запись', 'provisioning.account-disabled': 'В системе заблокирована учётная запись',
  'provisioning.account-enabled': 'В системе снова включена учётная запись', 'provisioning.membership-added': 'В системе добавлен в группу',
  'provisioning.membership-removed': 'В системе исключён из группы', 'provisioning.failed': 'Не удалось записать в систему', 'provisioning.obsolete': 'Действие стало ненужным',
}

/** Ruscha nomi yo'q audit amallari (test uchun: yangi amal qo'shilganda tarjima unutilmasin). */
export const untranslatedActions = () => Object.keys(actions).filter(a => !actionsRu[a])

/** Audit amali → odam tilidagi nom, ikonka, rang va tafsilotlardan qisqa xulosa (login, tizim, guruh...). */
export function describeAction(action: string, details: string | null): { label: string; icon: string; tone: Tone; summary: string } {
  const [uzLabel, icon, tone] = actions[action] ?? [action, 'audit', 'accent' as Tone]
  const label = language === 'ru' ? actionsRu[action] ?? uzLabel : uzLabel
  let summary = ''
  if (details) {
    try {
      const d = lowerKeys(JSON.parse(details) as Record<string, unknown>)
      const parts = ['application', 'login', 'employeenumber', 'beneficiary', 'group', 'code', 'name', 'role', 'step', 'department', 'jobtitle', 'comment', 'error']
        .map(k => d[k]).filter((v): v is string | number => typeof v === 'string' || typeof v === 'number')
      if (typeof d.accountsread === 'number') parts.push(t.audit.summary.accounts(d.accountsread))
      if (typeof d.records === 'number') parts.push(t.audit.summary.records(d.records))
      const changes = syncChanges(d)
      if (changes > 0) parts.push(t.audit.summary.changes(changes))
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
