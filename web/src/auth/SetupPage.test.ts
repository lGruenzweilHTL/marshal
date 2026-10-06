import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SetupPage from './SetupPage.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

describe('SetupPage', () => {
  it('claims the instance owner and opens the app', async () => {
    const server = new FakeServer().antiforgery('t').on('POST', '/api/v1/auth/local/setup', 204).on('GET', '/api/v1/me', 200, me)
    const { options, router } = await mountOptions(server, '/setup')
    const wrapper = mount(SetupPage, options)

    await wrapper.get('input[name="userName"]').setValue('lukas')
    await wrapper.get('input[name="displayName"]').setValue('Lukas')
    await wrapper.get('input[name="password"]').setValue('correct horse battery staple')
    await wrapper.get('input[name="repeat"]').setValue('correct horse battery staple')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(JSON.parse(server.request('POST', '/api/v1/auth/local/setup')!.body)).toEqual({
      userName: 'lukas',
      displayName: 'Lukas',
      password: 'correct horse battery staple',
    })
    expect(router.currentRoute.value.fullPath).toBe('/')
  })

  it('shows why the server rejected the setup', async () => {
    const server = new FakeServer()
      .antiforgery('t')
      .on('POST', '/api/v1/auth/local/setup', 400, { code: 'auth.setup_invalid', detail: 'Passwords must be at least 12 characters.' })
    const { options } = await mountOptions(server, '/setup')
    const wrapper = mount(SetupPage, options)

    await wrapper.get('input[name="userName"]').setValue('lukas')
    await wrapper.get('input[name="password"]').setValue('short')
    await wrapper.get('input[name="repeat"]').setValue('short')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('at least 12 characters')
  })

  it('shows the HTTPS problem in German and keeps the values', async () => {
    const server = new FakeServer()
      .antiforgery('t')
      .on('POST', '/api/v1/auth/local/setup', 400, { code: 'auth.https_required', detail: 'HTTPS is required.' })
    const { options } = await mountOptions(server, '/setup', { language: 'de' })
    const wrapper = mount(SetupPage, options)

    await wrapper.get('input[name="userName"]').setValue('lukas')
    await wrapper.get('input[name="password"]').setValue('correct horse battery staple')
    await wrapper.get('input[name="repeat"]').setValue('correct horse battery staple')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('HTTPS geöffnet')
    expect((wrapper.get('input[name="userName"]').element as HTMLInputElement).value).toBe('lukas')
    expect((wrapper.get('input[name="password"]').element as HTMLInputElement).value).toBe('correct horse battery staple')
  })

  it('does not send mismatched passwords', async () => {
    const server = new FakeServer().antiforgery('t')
    const { options } = await mountOptions(server, '/setup')
    const wrapper = mount(SetupPage, options)

    await wrapper.get('input[name="userName"]').setValue('lukas')
    await wrapper.get('input[name="password"]').setValue('correct horse battery staple')
    await wrapper.get('input[name="repeat"]').setValue('correct horse battery stapler')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(server.count('POST', '/api/v1/auth/local/setup')).toBe(0)
    expect(wrapper.text()).toContain('Passwords do not match')
  })
})
