import { useQuery } from '@tanstack/react-query'
import { api } from './client'
import type { Me } from './types'

/** Joriy foydalanuvchi va uning portal vakolatlari (Layout bilan bitta kesh). */
export function useMe() {
  return useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/api/me') })
}

export function useIsAdmin(): boolean {
  return useMe().data?.roles.includes('idm.admin') ?? false
}
