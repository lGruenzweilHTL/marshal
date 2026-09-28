import { inject, type InjectionKey } from 'vue'
import type { ApiSession } from './client'

export const apiKey: InjectionKey<ApiSession> = Symbol('api')

/** The API session provided by the app (tests provide one backed by a fake server). */
export function useApi(): ApiSession {
  const session = inject(apiKey)
  if (!session) throw new Error('No API session provided.')
  return session
}
