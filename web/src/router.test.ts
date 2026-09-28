import { describe, expect, it } from 'vitest'
import { createMemoryHistory } from 'vue-router'
import { createApiSession } from './api/client'
import { createAppRouter } from './router'
import { createUserSession } from './session'
import { FakeServer } from './testing/fakeServer'
import { me } from './testing/mountApp'

describe('router', () => {
  it('sends anonymous users to the login page with a return URL', async () => {
    const api = createApiSession({ baseUrl: 'https://marshal.test', fetch: new FakeServer().on('GET', '/api/v1/me', 401).fetch })
    const router = createAppRouter(createUserSession(api), createMemoryHistory())

    await router.push('/runs/7')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query).toEqual({ returnUrl: '/runs/7' })
  })

  it('lets signed-in users through', async () => {
    const api = createApiSession({ baseUrl: 'https://marshal.test', fetch: new FakeServer().on('GET', '/api/v1/me', 200, me).fetch })
    const router = createAppRouter(createUserSession(api), createMemoryHistory())

    await router.push('/runs/7')

    expect(router.currentRoute.value.fullPath).toBe('/runs/7')
  })
})
