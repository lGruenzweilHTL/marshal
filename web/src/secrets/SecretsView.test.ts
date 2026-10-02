import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import SecretsView from './SecretsView.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const overview = {
  secrets: [
    { name: 'anthropic_key', scope: 'instance', origin: 'environment', updatedAt: null, readOnly: true },
    { name: 'freshrss', scope: 'user', origin: 'database', updatedAt: '2026-09-20T10:00:00Z', readOnly: false },
    { name: 'shared_db', scope: 'instance', origin: 'database', updatedAt: '2026-09-21T10:00:00Z', readOnly: true },
  ],
  unresolved: [{ name: 'freshrss_api', referencedBy: ['marshal.yaml: feeds.apiKeySecret'] }],
}

function server(user = me) {
  return new FakeServer().antiforgery('t').on('GET', '/api/v1/me', 200, user).on('GET', '/api/v1/me/secrets', 200, overview)
}

async function mountSecrets(fake: FakeServer) {
  const { options } = await mountOptions(fake, '/secrets')
  const wrapper = mount(SecretsView, options)
  await flushPromises()
  return wrapper
}

describe('SecretsView', () => {
  afterEach(() => vi.restoreAllMocks())

  it('lists secrets with scope, source and update time, never a value', async () => {
    const wrapper = await mountSecrets(server())

    const env = wrapper.get('[data-secret="instance/anthropic_key"]')
    expect(env.text()).toContain('instance')
    expect(env.text()).toContain('environment')
    expect(env.find('[data-test="delete"]').exists()).toBe(false)
    expect(wrapper.get('[data-secret="user/freshrss"]').find('[data-test="delete"]').exists()).toBe(true)
    expect(wrapper.get('[data-secret="instance/shared_db"]').find('[data-test="delete"]').exists()).toBe(false)
  })

  it('flags unresolved references with where they are used', async () => {
    const wrapper = await mountSecrets(server())

    const flagged = wrapper.get('[data-unresolved="freshrss_api"]')
    expect(flagged.text()).toContain('marshal.yaml: feeds.apiKeySecret')
  })

  it('stores a user secret write-only and clears the value field', async () => {
    const fake = server().on('PUT', '/api/v1/me/secrets/freshrss_api', 204)
    const wrapper = await mountSecrets(fake)

    await wrapper.get('[data-unresolved="freshrss_api"] [data-test="set"]').trigger('click')
    expect((wrapper.get('input[name="secretName"]').element as HTMLInputElement).value).toBe('freshrss_api')
    await wrapper.get('input[name="secretValue"]').setValue('s3cret')
    await wrapper.get('form[data-test="set-secret"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('PUT', '/api/v1/me/secrets/freshrss_api')!.body)).toEqual({ value: 's3cret' })
    expect((wrapper.get('input[name="secretValue"]').element as HTMLInputElement).value).toBe('')
    expect(fake.count('GET', '/api/v1/me/secrets')).toBe(2)
  })

  it('lets admins store instance secrets', async () => {
    const fake = server({ ...me, roles: ['admin'] }).on('PUT', '/api/v1/secrets/instance/ha_token', 204)
    const wrapper = await mountSecrets(fake)

    await wrapper.get('input[name="secretName"]').setValue('ha_token')
    await wrapper.get('input[name="secretValue"]').setValue('v')
    await wrapper.get('select[name="secretScope"]').setValue('instance')
    await wrapper.get('form[data-test="set-secret"]').trigger('submit')
    await flushPromises()

    expect(fake.count('PUT', '/api/v1/secrets/instance/ha_token')).toBe(1)
  })

  it('offers no instance scope to non-admins', async () => {
    const wrapper = await mountSecrets(server())

    expect(wrapper.find('select[name="secretScope"]').exists()).toBe(false)
  })

  it('deletes a secret after confirmation', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true)
    const fake = server().on('DELETE', '/api/v1/me/secrets/freshrss', 204)
    const wrapper = await mountSecrets(fake)

    await wrapper.get('[data-secret="user/freshrss"] [data-test="delete"]').trigger('click')
    await flushPromises()

    expect(fake.count('DELETE', '/api/v1/me/secrets/freshrss')).toBe(1)
  })
})
