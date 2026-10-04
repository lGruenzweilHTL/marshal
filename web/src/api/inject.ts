import { inject, type InjectionKey } from 'vue'
import type { Api, ApiSession } from './client'

export const apiKey: InjectionKey<ApiSession> = Symbol('api')

/** The API provided by the app (tests provide one backed by a fake server). Views get {@link Api}, never the raw client. */
export function useApi(): Api {
  return useApiSession()
}

/**
 * The full session including the raw client.
 * @deprecated Only for views not yet rebuilt on {@link useApi}; the architecture test flags every use outside `web/src/api/`.
 */
export function useApiSession(): ApiSession {
  const session = inject(apiKey)
  if (!session) throw new Error('No API session provided.')
  return session
}
