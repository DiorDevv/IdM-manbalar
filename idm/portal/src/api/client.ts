import { currentUser, signIn } from '../auth/oidc'

export class ApiError extends Error {
  /** Validatsiya xatolari: maydon → xabarlar (ASP.NET ValidationProblem). */
  constructor(public status: number, message: string, public fieldErrors: Record<string, string[]> = {}) { super(message) }
}

/** IdM API'ga so'rov: SSO access tokeni bilan. 401 bo'lsa qayta kirishga yuboradi. */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const user = await currentUser()
  if (!user) { await signIn(); throw new ApiError(401, 'Kirish talab qilinadi') }
  const response = await fetch(path, {
    ...init,
    headers: { ...(init.body ? { 'Content-Type': 'application/json' } : {}), ...init.headers, Authorization: `Bearer ${user.access_token}`, Accept: 'application/json', 'X-Correlation-Id': crypto.randomUUID() },
  })
  if (response.status === 401) { await signIn(); throw new ApiError(401, 'Sessiya tugadi') }
  if (!response.ok) {
    const text = await response.text()
    let message = text
    let fieldErrors: Record<string, string[]> = {}
    try {
      const problem = JSON.parse(text) as { title?: string; detail?: string; errors?: Record<string, string[]> }
      fieldErrors = Object.fromEntries(Object.entries(problem.errors ?? {}).map(([k, v]) => [k.charAt(0).toLowerCase() + k.slice(1), v]))
      message = problem.detail ?? (Object.values(fieldErrors)[0]?.[0]) ?? problem.title ?? text
    } catch { /* matn qoldiriladi */ }
    throw new ApiError(response.status, message || `HTTP ${response.status}`, fieldErrors)
  }
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** Faylni yuklab olish (API token bilan himoyalangan, shuning uchun oddiy havola emas): server bergan nom bilan saqlanadi. */
export async function download(path: string, fallbackName: string): Promise<void> {
  const user = await currentUser()
  if (!user) { await signIn(); throw new ApiError(401, 'Kirish talab qilinadi') }
  const response = await fetch(path, { headers: { Authorization: `Bearer ${user.access_token}`, 'X-Correlation-Id': crypto.randomUUID() } })
  if (!response.ok) throw new ApiError(response.status, response.status === 403 ? 'Ruxsat yo\'q' : `HTTP ${response.status}`)
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const name = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1] ?? /filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? fallbackName
  const url = URL.createObjectURL(await response.blob())
  const a = document.createElement('a')
  a.href = url
  a.download = decodeURIComponent(name)
  a.click()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}

export { query } from './query'
