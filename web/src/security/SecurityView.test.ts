import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import SecurityView from './SecurityView.vue'
import { passkeysKey, type Passkeys } from '../auth/passkeys'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const user = { ...me, scopes: ['conversation:write', 'tasks:read', 'tasks:write'] }

function token(overrides: Record<string, unknown> = {}) {
  return { id: 't-1', name: 'Home Assistant', scopes: ['conversation:write'], kind: 'standard', createdAt: '2026-09-01T10:00:00Z', expiresAt: null, revokedAt: null, readOnly: false, ...overrides }
}

function server(providers = [{ name: 'local', type: 'local', displayName: 'Local account' }]) {
  return new FakeServer()
    .antiforgery('t')
    .on('GET', '/api/v1/me', 200, user)
    .on('GET', '/api/v1/auth/providers', 200, providers)
    .on('GET', '/api/v1/me/tokens', 200, [
      token(),
      token({ id: 't-2', name: 'old', revokedAt: '2026-09-02T10:00:00Z' }),
      token({ id: 't-3', name: 'executor', kind: 'executorCallback', readOnly: true }),
    ])
}

const passkeys: Passkeys = { supported: () => true, get: async () => '{}', create: async () => '{"id":"new-key"}' }

async function mountSecurity(fake: FakeServer) {
  const mounted = await mountOptions(fake, '/security')
  const wrapper = mount(SecurityView, { global: { ...mounted.options.global, provide: { ...mounted.options.global.provide, [passkeysKey as symbol]: passkeys } } })
  await flushPromises()
  return wrapper
}

describe('SecurityView', () => {
  afterEach(() => vi.restoreAllMocks())

  it('lists tokens; only active standard tokens can be revoked', async () => {
    const wrapper = await mountSecurity(server())

    expect(wrapper.get('[data-token="t-1"]').text()).toContain('conversation:write')
    expect(wrapper.get('[data-token="t-1"]').find('[data-test="revoke"]').exists()).toBe(true)
    expect(wrapper.get('[data-token="t-2"]').text()).toContain('revoked')
    expect(wrapper.get('[data-token="t-2"]').find('[data-test="revoke"]').exists()).toBe(false)
    expect(wrapper.get('[data-token="t-3"]').text()).toContain('read-only')
    expect(wrapper.get('[data-token="t-3"]').find('[data-test="revoke"]').exists()).toBe(false)
  })

  it('creates a token with the user’s own scopes and shows the plaintext once', async () => {
    const fake = server().on('POST', '/api/v1/me/tokens', 201, { info: token({ id: 't-9', name: 'script' }), token: 'mrs_secret_value' })
    const wrapper = await mountSecurity(fake)

    expect(wrapper.findAll('input[name="scope"]').map((input) => input.attributes('value'))).toEqual(user.scopes)
    await wrapper.get('input[name="tokenName"]').setValue('script')
    await wrapper.get('input[name="scope"][value="tasks:read"]').setValue(true)
    await wrapper.get('form[data-test="create-token"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/me/tokens')!.body)).toEqual({ name: 'script', scopes: ['tasks:read'], expiresAt: null, kind: null })
    expect(wrapper.get('[data-test="new-token"]').text()).toContain('mrs_secret_value')
    expect(fake.count('GET', '/api/v1/me/tokens')).toBe(2)

    await wrapper.get('[data-test="dismiss-token"]').trigger('click')
    expect(wrapper.text()).not.toContain('mrs_secret_value')
  })

  it('sends the expiry as the end of the chosen day', async () => {
    const fake = server().on('POST', '/api/v1/me/tokens', 201, { info: token(), token: 'x' })
    const wrapper = await mountSecurity(fake)

    await wrapper.get('input[name="tokenName"]').setValue('temp')
    await wrapper.get('input[name="scope"][value="conversation:write"]').setValue(true)
    await wrapper.get('input[name="expiresAt"]').setValue('2026-12-31')
    await wrapper.get('form[data-test="create-token"]').trigger('submit')
    await flushPromises()

    const expiresAt = JSON.parse(fake.request('POST', '/api/v1/me/tokens')!.body).expiresAt as string
    expect(new Date(expiresAt).getTime()).toBe(new Date(2026, 11, 31, 23, 59, 59).getTime())
  })

  it('revokes a token after confirmation', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const fake = server().on('DELETE', '/api/v1/me/tokens/t-1', 204)
    const wrapper = await mountSecurity(fake)

    await wrapper.get('[data-token="t-1"] [data-test="revoke"]').trigger('click')
    await flushPromises()

    expect(fake.count('DELETE', '/api/v1/me/tokens/t-1')).toBe(1)
    expect(fake.count('GET', '/api/v1/me/tokens')).toBe(2)
  })

  it('adds a passkey when the local provider is active', async () => {
    const fake = server()
      .on('POST', '/api/v1/auth/local/passkeys/creation-options', 200, { challenge: 'c' })
      .on('POST', '/api/v1/auth/local/passkeys', 204)
    const wrapper = await mountSecurity(fake)

    await wrapper.get('input[name="passkeyName"]').setValue('Phone')
    await wrapper.get('form[data-test="add-passkey"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/auth/local/passkeys')!.body)).toEqual({ credentialJson: '{"id":"new-key"}', name: 'Phone' })
    expect(wrapper.find('[data-test="passkey-added"]').exists()).toBe(true)
  })

  it('hides passkeys without a local provider', async () => {
    const wrapper = await mountSecurity(server([{ name: 'authentik', type: 'oidc', displayName: 'Authentik' }]))

    expect(wrapper.find('form[data-test="add-passkey"]').exists()).toBe(false)
  })
})
