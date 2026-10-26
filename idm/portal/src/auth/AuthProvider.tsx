import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import type { User } from 'oidc-client-ts'
import { currentUser, signIn, signOut, userManager } from './oidc'
import { t } from '../i18n'

interface AuthState {
  user: User
  signOut: () => Promise<void>
}

const AuthContext = createContext<AuthState | null>(null)

// Avtorizatsiya kodi faqat bir marta almashtiriladi: React StrictMode effektni ikki marta chaqiradi,
// ikkinchi almashtirishni IdServer to'g'ri rad etadi ("bad authorization code").
let callbackOnce: Promise<User> | null = null
function completeSignIn(): Promise<User> {
  callbackOnce ??= userManager.signinRedirectCallback()
  return callbackOnce
}

export function useAuth(): AuthState {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth: AuthProvider ichida ishlatilishi kerak')
  return value
}

/** Foydalanuvchi kirmagan bo'lsa, SSO login sahifasiga yuboradi. /callback'da kodni tokenga almashtiradi. */
export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    async function run() {
      try {
        if (window.location.pathname === '/callback') {
          const signedIn = await completeSignIn()
          const returnTo = (signedIn.state as { returnTo?: string } | undefined)?.returnTo ?? '/'
          window.history.replaceState(null, '', returnTo.startsWith('/') && !returnTo.startsWith('//') ? returnTo : '/')
          if (!cancelled) setUser(signedIn)
          return
        }
        const existing = await currentUser()
        if (existing) { if (!cancelled) setUser(existing); return }
        await signIn()
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : String(e))
      }
    }
    void run()
    const onExpired = () => { void signIn() }
    userManager.events.addAccessTokenExpired(onExpired)
    return () => { cancelled = true; userManager.events.removeAccessTokenExpired(onExpired) }
  }, [])

  if (error) {
    return (
      <div className="center-screen">
        <div className="card" style={{ maxWidth: 480, padding: 24 }}>
          <h2 style={{ marginTop: 0, fontSize: 16 }}>{t.errorTitle}</h2>
          <div className="alert bad">{error}</div>
          <p><button className="btn primary" onClick={() => { void userManager.removeUser().then(() => signIn('/')) }}>{t.retry}</button></p>
        </div>
      </div>
    )
  }
  if (!user) return <div className="center-screen"><div style={{ display: 'flex', gap: 10, alignItems: 'center' }}><span className="spinner" />{t.signingIn}</div></div>
  return <AuthContext.Provider value={{ user, signOut }}>{children}</AuthContext.Provider>
}
