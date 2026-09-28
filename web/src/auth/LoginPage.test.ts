import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import LoginPage from './LoginPage.vue'
import { FakeServer } from '../testing/fakeServer'
import { mountOptions } from '../testing/mountApp'

function server(setup = 404) {
  return new FakeServer()
    .on('GET', '/api/v1/auth/providers', 200, [
      { name: 'authentik', type: 'oidc', displayName: 'Authentik' },
      { name: 'local', type: 'local', displayName: 'Local account' },
    ])
    .on('GET', '/api/v1/auth/local/setup', setup, setup === 200 ? { available: true } : undefined)
}

describe('LoginPage', () => {
  it('offers one sign-in per provider, keeping the return URL', async () => {
    const { options } = await mountOptions(server(), '/login?returnUrl=%2Fruns%2F1')
    const wrapper = mount(LoginPage, options)
    await flushPromises()

    const oidc = wrapper.get('a[data-provider="authentik"]')
    expect(oidc.text()).toContain('Authentik')
    expect(oidc.attributes('href')).toBe('https://marshal.test/api/v1/auth/login/authentik?returnUrl=%2Fruns%2F1')
    const local = wrapper.get('a[data-provider="local"]')
    expect(local.attributes('href')).toBe('/login/local?returnUrl=/runs/1')
  })

  it('links to the first-run setup while it is available', async () => {
    const { options } = await mountOptions(server(200), '/login')
    const wrapper = mount(LoginPage, options)
    await flushPromises()

    expect(wrapper.find('a[href="/setup"]').exists()).toBe(true)
  })

  it('hides the setup link once the instance is claimed', async () => {
    const { options } = await mountOptions(server(404), '/login')
    const wrapper = mount(LoginPage, options)
    await flushPromises()

    expect(wrapper.find('a[href="/setup"]').exists()).toBe(false)
  })

  it('explains a denied login', async () => {
    const { options } = await mountOptions(server(), '/login?error=access_denied')
    const wrapper = mount(LoginPage, options)
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('no access')
  })
})
