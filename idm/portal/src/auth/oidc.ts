import { UserManager, WebStorageStateStore, type User } from 'oidc-client-ts'

// SSO (SimpleIdServer) bilan avtorizatsiya kodi + PKCE. Maxfiy kalit yo'q: portal ochiq (public) ilova.
const authority = import.meta.env.VITE_SSO_AUTHORITY ?? 'https://localhost:5001/master'
const origin = window.location.origin

export const userManager = new UserManager({
  authority,
  client_id: import.meta.env.VITE_SSO_CLIENT_ID ?? 'idm-portal',
  redirect_uri: `${origin}/callback`,
  post_logout_redirect_uri: `${origin}/`,
  response_type: 'code',
  scope: 'openid profile role idm',
  // Token faqat shu brauzer oynasi uchun (sessionStorage): oyna yopilsa qayta kirish kerak
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  automaticSilentRenew: false,
  loadUserInfo: true,
})

export async function currentUser(): Promise<User | null> {
  const user = await userManager.getUser()
  return user && !user.expired ? user : null
}

export function signIn(returnTo = window.location.pathname + window.location.search) {
  return userManager.signinRedirect({ state: { returnTo } })
}

/** Qo'shimcha tasdiqlash: SSO'ga ikki bosqichli kirish darajasi (ACR) bilan qayta yuborish. */
export function signInWithMfa(acr: string, returnTo = window.location.pathname + window.location.search) {
  return userManager.signinRedirect({ state: { returnTo }, acr_values: acr })
}

export function signOut() {
  return userManager.signoutRedirect()
}
