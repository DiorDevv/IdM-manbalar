import { currentUser, signIn } from '../auth/oidc'

export class ApiError extends Error {
  constructor(public status: number, message: string) { super(message) }
}

/** IdM API'ga so'rov: SSO access tokeni bilan. 401 bo'lsa qayta kirishga yuboradi. */
export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const user = await currentUser()
  if (!user) { await signIn(); throw new ApiError(401, 'Kirish talab qilinadi') }
  const response = await fetch(path, {
    ...init,
    headers: { ...init.headers, Authorization: `Bearer ${user.access_token}`, Accept: 'application/json', 'X-Correlation-Id': crypto.randomUUID() },
  })
  if (response.status === 401) { await signIn(); throw new ApiError(401, 'Sessiya tugadi') }
  if (!response.ok) {
    const text = await response.text()
    let message = text
    try { const problem = JSON.parse(text) as { title?: string; detail?: string }; message = problem.detail ?? problem.title ?? text } catch { /* matn qoldiriladi */ }
    throw new ApiError(response.status, message || `HTTP ${response.status}`)
  }
  return (await response.json()) as T
}

export { query } from './query'
