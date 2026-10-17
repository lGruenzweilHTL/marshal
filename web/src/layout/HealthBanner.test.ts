import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import type { Schemas } from '../api/client'
import HealthBanner from './HealthBanner.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

type Warning = Schemas['HealthWarningView']

const pending: Warning = {
  component: 'mcp:paperless',
  code: 'mcp.tool_pending',
  severity: 'warning',
  message: { de: 'Ein Werkzeug von paperless wartet auf Freigabe.', en: 'A tool of paperless waits for approval.' },
  link: '/system/config',
  since: '2026-10-17T08:00:00Z',
}

const database: Warning = {
  component: 'database',
  code: 'database.unreachable',
  severity: 'error',
  message: { de: 'Die Datenbank ist nicht erreichbar.', en: 'The database is unreachable.' },
  link: null,
  since: '2026-10-17T08:05:00Z',
}

async function banner(warnings: Warning[], options: { admin?: boolean; language?: 'en' | 'de' } = {}) {
  const user = options.admin ? { ...me, roles: ['user', 'admin'] } : me
  const server = new FakeServer().on('GET', '/api/v1/me', 200, user).on('GET', '/api/v1/system/health', 200, { warnings })
  const mounted = await mountOptions(server, '/chat', { language: options.language })
  await mounted.user.load()
  const wrapper = mount(HealthBanner, mounted.options)
  await flushPromises()
  return { wrapper, ...mounted }
}

describe('HealthBanner', () => {
  afterEach(() => localStorage.clear())

  it('stays hidden without warnings', async () => {
    const { wrapper } = await banner([])

    expect(wrapper.find('[data-test=health-banner]').exists()).toBe(false)
  })

  it('shows the count, the first message and a link to the system view', async () => {
    const { wrapper } = await banner([pending, { ...pending, component: 'mcp:ha', code: 'mcp.unreachable' }])

    const shown = wrapper.get('[data-test=health-banner]')
    expect(shown.classes()).toContain('warning')
    expect(shown.text()).toContain('2 warnings need attention')
    expect(shown.text()).toContain('A tool of paperless waits for approval.')
    expect(wrapper.get('[data-test=health-details]').attributes('href')).toBe('/system')
    expect(wrapper.find('[data-test=health-open]').exists()).toBe(false)
  })

  it('takes the highest severity color and gives admins the warning link', async () => {
    const { wrapper } = await banner([database, pending], { admin: true })

    expect(wrapper.get('[data-test=health-banner]').classes()).toContain('danger')
    expect(wrapper.get('[data-test=health-banner]').text()).toContain('The database is unreachable.')
    expect(wrapper.find('[data-test=health-open]').exists()).toBe(false)

    const single = await banner([pending], { admin: true })
    expect(single.wrapper.get('[data-test=health-open]').attributes('href')).toBe('/system/config')
  })

  it('speaks German', async () => {
    const { wrapper } = await banner([pending], { language: 'de' })

    expect(wrapper.text()).toContain('1 Warnung braucht Aufmerksamkeit')
    expect(wrapper.text()).toContain('Ein Werkzeug von paperless wartet auf Freigabe.')
  })

  it('stays dismissed for the same warning codes and returns when they change', async () => {
    const { wrapper, live } = await banner([pending])

    await wrapper.get('[data-test=dismiss]').trigger('click')
    expect(wrapper.find('[data-test=health-banner]').exists()).toBe(false)

    const again = await banner([{ ...pending, since: '2026-10-17T09:00:00Z' }])
    expect(again.wrapper.find('[data-test=health-banner]').exists()).toBe(false)

    live.publish('health', 'health.changed', { warnings: [pending, database] })
    await flushPromises()
    expect(wrapper.get('[data-test=health-banner]').text()).toContain('2 warnings need attention')
  })

  it('updates from the health topic', async () => {
    const { wrapper, live } = await banner([])

    live.publish('health', 'health.changed', { warnings: [pending] })
    await flushPromises()
    expect(wrapper.get('[data-test=health-banner]').text()).toContain('A tool of paperless waits for approval.')

    live.publish('health', 'health.changed', { warnings: [] })
    await flushPromises()
    expect(wrapper.find('[data-test=health-banner]').exists()).toBe(false)
  })
})
