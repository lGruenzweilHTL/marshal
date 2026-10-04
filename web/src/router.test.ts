import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { defineComponent, h } from 'vue'
import { createMemoryHistory, RouterView } from 'vue-router'
import { createApiSession } from './api/client'
import { apiKey } from './api/inject'
import { createAppI18n } from './i18n'
import { createAppRouter } from './router'
import { createUserSession, sessionKey } from './session'
import { FakeServer } from './testing/fakeServer'
import { me } from './testing/mountApp'

function setup(server: FakeServer) {
  const api = createApiSession({ baseUrl: 'https://marshal.test', fetch: server.fetch })
  const user = createUserSession(api)
  return { api, user, router: createAppRouter(user, createMemoryHistory()) }
}

describe('router', () => {
  it('sends anonymous users to the login page with a return URL', async () => {
    const { router } = setup(new FakeServer().on('GET', '/api/v1/me', 401))

    await router.push('/runs/7')

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query).toEqual({ returnUrl: '/runs/7' })
  })

  it('lets signed-in users through', async () => {
    const { router } = setup(new FakeServer().on('GET', '/api/v1/me', 200, me))

    await router.push('/runs/7')

    expect(router.currentRoute.value.fullPath).toBe('/runs/7')
  })

  it('returns to the login page with "expired" when a request after sign-in gets 401', async () => {
    const server = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/tasks', 401)
    const { api, router } = setup(server)
    await router.push('/tasks?state=active')

    await api.call((client) => client.GET('/api/v1/tasks'))
    await flushPromises()

    expect(router.currentRoute.value.path).toBe('/login')
    expect(router.currentRoute.value.query).toEqual({ reason: 'expired', returnUrl: '/tasks?state=active' })
  })

  it('shows a full-page error with retry when /me fails with another status', async () => {
    const server = new FakeServer().on('GET', '/api/v1/me', 503, { detail: 'Starting.' })
    const { api, user, router } = setup(server)
    await router.push('/tasks')
    expect(router.currentRoute.value.name).toBe('unavailable')
    expect(router.currentRoute.value.query).toEqual({ returnUrl: '/tasks' })

    const App = defineComponent({ render: () => h(RouterView) })
    const wrapper = mount(App, {
      global: { plugins: [router, createAppI18n()], provide: { [apiKey as symbol]: api, [sessionKey as symbol]: user } },
      attachTo: document.body,
    })
    await flushPromises()
    expect(wrapper.get('[role=alert]').text()).toContain('Starting.')

    server.on('GET', '/api/v1/me', 200, me)
    await wrapper.get('[data-test="retry"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/tasks')
    wrapper.unmount()
  })
})
