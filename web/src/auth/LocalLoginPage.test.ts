import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import LocalLoginPage from './LocalLoginPage.vue'
import { passkeysKey, type Passkeys } from './passkeys'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

class FakePasskeys implements Passkeys {
  readonly requested: string[] = []
  constructor(private readonly available = true) {}
  supported() {
    return this.available
  }
  async get(optionsJson: string) {
    this.requested.push(optionsJson)
    return '{"id":"cred-1"}'
  }
  async create() {
    return '{"id":"new"}'
  }
}

async function mountPage(server: FakeServer, path: string, passkeys: Passkeys = new FakePasskeys()) {
  const mounted = await mountOptions(server, path)
  const wrapper = mount(LocalLoginPage, { global: { ...mounted.options.global, provide: { ...mounted.options.global.provide, [passkeysKey as symbol]: passkeys } } })
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('LocalLoginPage', () => {
  it('signs in with a password and returns to the deep link', async () => {
    const server = new FakeServer().antiforgery('t-1').on('POST', '/api/v1/auth/local/login', 204).on('GET', '/api/v1/me', 200, me)
    const { wrapper, router, user } = await mountPage(server, '/login/local?returnUrl=%2Fruns%2F7')

    await wrapper.get('input[name="userName"]').setValue('lukas')
    await wrapper.get('input[name="password"]').setValue('correct horse battery staple')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const login = server.request('POST', '/api/v1/auth/local/login')!
    expect(JSON.parse(login.body)).toEqual({ userName: 'lukas', password: 'correct horse battery staple' })
    expect(login.headers.get('X-XSRF-TOKEN')).toBe('t-1')
    expect(router.currentRoute.value.fullPath).toBe('/runs/7')
    expect(user.current.value?.displayName).toBe('Lukas')
  })

  it('shows an error for wrong credentials', async () => {
    const server = new FakeServer().antiforgery('t').on('POST', '/api/v1/auth/local/login', 401, { code: 'auth.invalid_credentials' })
    const { wrapper, router } = await mountPage(server, '/login/local')

    await wrapper.get('input[name="userName"]').setValue('lukas')
    await wrapper.get('input[name="password"]').setValue('nope')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('User name or password is wrong')
    expect(router.currentRoute.value.path).toBe('/login/local')
  })

  it('never returns to another origin', async () => {
    const server = new FakeServer().antiforgery('t').on('POST', '/api/v1/auth/local/login', 204).on('GET', '/api/v1/me', 200, me)
    const { wrapper, router } = await mountPage(server, '/login/local?returnUrl=%2F%2Fevil.example%2Fx')

    await wrapper.get('input[name="userName"]').setValue('lukas')
    await wrapper.get('input[name="password"]').setValue('pw')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/')
  })

  it('signs in with a passkey', async () => {
    const passkeys = new FakePasskeys()
    const server = new FakeServer()
      .antiforgery('t')
      .on('POST', '/api/v1/auth/local/passkey/options', 200, { challenge: 'abc' })
      .on('POST', '/api/v1/auth/local/passkey', 204)
      .on('GET', '/api/v1/me', 200, me)
    const { wrapper, router } = await mountPage(server, '/login/local', passkeys)

    await wrapper.get('[data-test="passkey"]').trigger('click')
    await flushPromises()

    expect(passkeys.requested).toEqual(['{"challenge":"abc"}'])
    expect(JSON.parse(server.request('POST', '/api/v1/auth/local/passkey')!.body)).toEqual({ credentialJson: '{"id":"cred-1"}' })
    expect(router.currentRoute.value.fullPath).toBe('/')
  })

  it('hides the passkey sign-in when the browser has no WebAuthn', async () => {
    const { wrapper } = await mountPage(new FakeServer(), '/login/local', new FakePasskeys(false))

    expect(wrapper.find('[data-test="passkey"]').exists()).toBe(false)
  })
})
