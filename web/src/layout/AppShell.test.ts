import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import AppShell from './AppShell.vue'
import { FakeServer } from '../testing/fakeServer'
import { mountOptions, me } from '../testing/mountApp'

describe('AppShell', () => {
  afterEach(() => vi.restoreAllMocks())

  it('shows the signed-in user and the navigation', async () => {
    const { options } = await mountOptions(new FakeServer().on('GET', '/api/v1/me', 200, me), '/')
    const wrapper = mount(AppShell, { global: { ...options.global, stubs: { RouterView: true } } })
    await flushPromises()

    expect(wrapper.get('[data-test="user"]').text()).toBe('Lukas')
    const links = wrapper.findAll('nav a').map((link) => link.text())
    expect(links).toEqual(['Dashboard', 'Chat', 'Memory', 'Runs', 'Cost', 'Security', 'Secrets', 'Preferences', 'System'])
  })

  it('shows the config link to admins only', async () => {
    const admin = { ...me, roles: ['admin'] }
    const { options } = await mountOptions(new FakeServer().on('GET', '/api/v1/me', 200, admin), '/')
    const wrapper = mount(AppShell, { global: { ...options.global, stubs: { RouterView: true } } })
    await flushPromises()

    expect(wrapper.findAll('nav a').map((link) => link.text())).toContain('Config')
  })

  it('logs out with a form post carrying the antiforgery token', async () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    const { options } = await mountOptions(new FakeServer().on('GET', '/api/v1/me', 200, me).antiforgery('t-9'), '/')
    const wrapper = mount(AppShell, { global: { ...options.global, stubs: { RouterView: true } } })
    await flushPromises()

    await wrapper.get('[data-test="logout"]').trigger('click')
    await flushPromises()

    expect(submit).toHaveBeenCalledOnce()
    const form = submit.mock.contexts[0] as HTMLFormElement
    expect(form.method).toBe('post')
    expect(form.action).toBe('https://marshal.test/api/v1/auth/logout')
    expect((form.elements.namedItem('__RequestVerificationToken') as HTMLInputElement).value).toBe('t-9')
  })

  it('toggles the navigation on small screens', async () => {
    const { options } = await mountOptions(new FakeServer().on('GET', '/api/v1/me', 200, me), '/')
    const wrapper = mount(AppShell, { global: { ...options.global, stubs: { RouterView: true } } })
    await flushPromises()

    await wrapper.get('[data-test="menu"]').trigger('click')

    expect(wrapper.get('nav').classes()).toContain('open')
  })
})
