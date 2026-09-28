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
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('at least 12 characters')
  })
})
