import { inject, shallowRef, type InjectionKey, type Ref } from 'vue'
import type { Api, ApiResult, Schemas } from './api/client'

export type Me = Schemas['MeResponse']

/** The signed-in user, loaded once from `/api/v1/me` (null while signed out). */
export interface UserSession {
  readonly current: Readonly<Ref<Me | null>>
  /** Loads the user on first use; later calls return the cached result. A 401 is `data: null`, not an error. */
  load(): Promise<ApiResult<Me | null>>
  /** Reloads the user after a login changed the session. */
  refresh(): Promise<ApiResult<Me | null>>
  /** Called once when a request of a signed-in user returns 401 (the cookie expired). */
  onExpired(listener: () => void): void
}

export const sessionKey: InjectionKey<UserSession> = Symbol('session')

export function createUserSession(api: Api): UserSession {
  const current = shallowRef<Me | null>(null)
  let loading: Promise<ApiResult<Me | null>> | null = null
  let expired: (() => void) | null = null

  async function fetchMe(): Promise<ApiResult<Me | null>> {
    const result = await api.call((client) => client.GET('/api/v1/me'))
    if (result.error) {
      loading = null
      if (result.error.kind === 'problem' && result.error.status === 401) return { data: (current.value = null) }
      return result
    }
    return { data: (current.value = result.data) }
  }

  api.onUnauthorized(() => {
    if (!current.value) return
    current.value = null
    loading = null
    api.resetAntiforgery()
    expired?.()
  })

  return {
    current,
    load() {
      loading ??= fetchMe()
      return loading
    },
    refresh() {
      api.resetAntiforgery()
      loading = fetchMe()
      return loading
    },
    onExpired(listener) {
      expired = listener
    },
  }
}

export function useUserSession(): UserSession {
  const user = inject(sessionKey)
  if (!user) throw new Error('No user session provided.')
  return user
}
