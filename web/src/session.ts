import { inject, shallowRef, type InjectionKey, type Ref } from 'vue'
import type { ApiSession, Schemas } from './api/client'

export type Me = Schemas['MeResponse']

/** The signed-in user, loaded once from `/api/v1/me` (null while signed out). */
export interface UserSession {
  readonly current: Readonly<Ref<Me | null>>
  /** Loads the user on first use; later calls return the cached result. */
  load(): Promise<Me | null>
  /** Reloads the user after a login changed the session. */
  refresh(): Promise<Me | null>
}

export const sessionKey: InjectionKey<UserSession> = Symbol('session')

export function createUserSession(session: ApiSession): UserSession {
  const current = shallowRef<Me | null>(null)
  let loading: Promise<Me | null> | null = null

  async function fetchMe(): Promise<Me | null> {
    const { data, response } = await session.api.GET('/api/v1/me')
    if (response.status === 401) return (current.value = null)
    if (!data) throw new Error(`/me: HTTP ${response.status}`)
    return (current.value = data)
  }

  return {
    current,
    load() {
      loading ??= fetchMe().catch((error: unknown) => {
        loading = null
        throw error
      })
      return loading
    },
    refresh() {
      session.resetAntiforgery()
      loading = fetchMe()
      return loading
    },
  }
}

export function useUserSession(): UserSession {
  const user = inject(sessionKey)
  if (!user) throw new Error('No user session provided.')
  return user
}
