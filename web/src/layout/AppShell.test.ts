import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import AppShell from './AppShell.vue'
import { FakeServer } from '../testing/fakeServer'
import { mountOptions, me } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

function server(user = me) {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, user)
    .on('GET', '/api/v1/approvals', 200, [{ id: 'a-1' }, { id: 'a-2' }])
    .on('GET', '/api/v1/proposals', 200, [{ id: 'p-1', status: 'open' }, { id: 'p-2', status: 'merged' }])
    .on('GET', '/api/v1/skill-proposals', 200, [{ id: 'sp-1', status: 'open' }, { id: 'sp-2', status: 'downloaded' }])
    .on('GET', '/api/v1/notifications', 200, [{ id: 'n-1', priority: 'critical', createdAt: new Date().toISOString() }, { id: 'n-2', priority: 'normal', createdAt: new Date().toISOString() }])
    .on('GET', '/api/v1/system/health', 200, { warnings: [] })
    .antiforgery('t-9')
}

async function shell(path = '/', fake = server(), language: 'en' | 'de' = 'en') {
  const mounted = await mountOptions(fake, path, { language })
  const wrapper = mount(AppShell, { global: { ...mounted.options.global, stubs: { RouterView: true } }, attachTo: document.body })
  await flushPromises()
  return { wrapper, ...mounted }
}

describe('AppShell', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    localStorage.clear()
    document.body.innerHTML = ''
  })

  it('shows health warnings above every page', async () => {
    useViewport('desktop')
    const fake = server({ ...me, roles: ['user', 'admin'] }).on('GET', '/api/v1/system/health', 200, {
      warnings: [{ component: 'mcp:paperless', code: 'mcp.tool_pending', severity: 'warning', message: { de: 'Wartet auf Freigabe.', en: 'Waits for approval.' }, link: '/system/config', since: '2026-10-17T08:00:00Z' }],
    })
    const { wrapper } = await shell('/chat', fake)

    expect(wrapper.get('.main [data-test=health-banner]').text()).toContain('Waits for approval.')
    expect(wrapper.get('[data-test=health-open]').attributes('href')).toBe('/system/config')
  })

  it('shows the grouped side navigation from 900 px', async () => {
    useViewport('desktop')
    const { wrapper } = await shell('/tasks')

    expect(wrapper.findAll('.group-label').map((label) => label.text())).toEqual(['Act', 'Activity', 'Memory', 'Connections', 'Settings'])
    expect(wrapper.findAll('[data-test^=nav-]').map((link) => link.text().replace(/\d+$/, ''))).toEqual([
      'Home', 'Chat', 'Inbox', 'Printer', 'Tasks', 'Runs', 'Artifacts', 'Cost', 'Chunks', 'Projects', 'Map', 'Audit', 'Mail accounts', 'Preferences', 'Security', 'System',
    ])
    expect(wrapper.get('[data-test=nav-tasks]').attributes('aria-current')).toBe('page')
    expect(wrapper.find('.tabbar').exists()).toBe(false)
  })

  it('shows the tab bar with five tabs below 900 px, More opens the rest', async () => {
    useViewport('phone')
    const { wrapper } = await shell('/runs')

    expect(wrapper.findAll('.tab').map((tab) => tab.text().replace(/^\d+/, ''))).toEqual(['Home', 'Chat', 'Inbox', 'Printer', 'More'])
    expect(wrapper.get('[data-test=tab-more]').attributes('aria-current')).toBe('page')
    expect(wrapper.find('.sidenav').exists()).toBe(false)

    await wrapper.get('[data-test=tab-more]').trigger('click')
    await flushPromises()
    expect(document.querySelector('[data-test=more-runs]')?.textContent).toContain('Runs')
  })

  it('counts pending approvals, open change and skill proposals and unseen critical notifications on the Inbox', async () => {
    useViewport('phone')
    const { wrapper } = await shell()

    expect(wrapper.get('[data-test=tab-inbox] .count').text()).toBe('5')
    expect(wrapper.get('[data-test=tab-inbox] .count').attributes('aria-label')).toBe('5 items need you')
  })

  it('lists Configuration for admins only', async () => {
    useViewport('desktop')
    const user = await shell()
    const admin = await shell('/', server({ ...me, roles: ['admin'] }))

    expect(user.wrapper.find('[data-test=nav-config]').exists()).toBe(false)
    expect(admin.wrapper.get('[data-test=nav-config]').text()).toContain('Configuration')
  })

  it('is German for a German user', async () => {
    useViewport('desktop')
    const { wrapper } = await shell('/', server(), 'de')

    expect(wrapper.get('[data-test=nav-inbox]').text()).toContain('Eingang')
    expect(wrapper.get('nav').attributes('aria-label')).toBe('Hauptnavigation')
  })

  it.each(['desktop', 'phone'] as const)('shows the Demo badge in demo mode (%s)', async (layout) => {
    useViewport(layout)
    const { wrapper } = await shell('/', server({ ...me, demo: { enabled: true, showBadge: true } }))

    expect(wrapper.get('[data-test=demo-badge]').text()).toBe('Demo')
    expect(wrapper.get('[data-test=demo-badge]').attributes('title')).toContain('simulated')
  })

  it.each(['desktop', 'phone'] as const)('hides the Demo badge with showBadge off (%s)', async (layout) => {
    useViewport(layout)
    const { wrapper } = await shell('/', server({ ...me, demo: { enabled: true, showBadge: false } }))

    expect(wrapper.find('[data-test=demo-badge]').exists()).toBe(false)
  })

  it('shows no Demo badge without demo mode', async () => {
    useViewport('desktop')
    const { wrapper } = await shell()

    expect(wrapper.find('[data-test=demo-badge]').exists()).toBe(false)
  })

  it('signs out with a form post carrying the antiforgery token', async () => {
    useViewport('desktop')
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => {})
    const { wrapper } = await shell()

    await wrapper.get('[data-test=logout]').trigger('click')
    await flushPromises()

    expect(submit).toHaveBeenCalledOnce()
    const form = submit.mock.contexts[0] as HTMLFormElement
    expect(form.method).toBe('post')
    expect(form.action).toBe('https://marshal.test/api/v1/auth/logout')
    expect((form.elements.namedItem('__RequestVerificationToken') as HTMLInputElement).value).toBe('t-9')
  })
})
