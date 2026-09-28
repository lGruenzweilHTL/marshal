import { createMemoryHistory, type Router } from 'vue-router'
import { createApiSession, type ApiSession } from '../api/client'
import { apiKey } from '../api/inject'
import { createAppRouter } from '../router'
import { createUserSession, sessionKey, type UserSession } from '../session'
import type { FakeServer } from './fakeServer'

export const me = { id: '0199aaaa-0000-7000-8000-000000000001', displayName: 'Lukas', roles: ['user'], scopes: ['conversation:write'] }

/** Global mount options for a component under test: API session on the fake server, router at `path`. */
export async function mountOptions(server: FakeServer, path: string) {
  const api: ApiSession = createApiSession({ baseUrl: 'https://marshal.test', fetch: server.fetch })
  const user: UserSession = createUserSession(api)
  const router: Router = createAppRouter(user, createMemoryHistory())
  await router.push(path)
  await router.isReady()
  return {
    api,
    user,
    router,
    options: { global: { plugins: [router], provide: { [apiKey as symbol]: api, [sessionKey as symbol]: user } } },
  }
}
