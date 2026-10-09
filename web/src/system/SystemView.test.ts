import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import SystemView from './SystemView.vue'
import { FakeServer } from '../testing/fakeServer'
import { health } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

const info = {
  version: '0.1.0+abc',
  supportedApiVersions: ['1.0'],
  deprecatedApiVersions: [] as string[],
  eventSources: [
    { source: 'ha', connected: true, webhook: false, lastReceivedAt: null },
    { source: 'mail', connected: false, webhook: false, lastReceivedAt: null },
    { source: 'uptime-kuma', connected: true, webhook: true, lastReceivedAt: '2026-10-08T08:15:00Z' },
  ],
}

function server(user = me) {
  return new FakeServer()
    .antiforgery('t')
    .on('GET', '/api/v1/me', 200, user)
    .on('GET', '/api/v1/system', 200, info)
    .on('GET', '/health', 200, health())
    .on('GET', '/api/v1/tasks', 200, [])
    .on('GET', '/api/v1/tasks/kill-switch', 200, { active: false })
}

async function mountSystem(fake: FakeServer, language: 'en' | 'de' = 'en') {
  const { options } = await mountOptions(fake, '/system', { language })
  const wrapper = mount(SystemView, { ...options, attachTo: document.body })
  await flushPromises()
  return wrapper
}

describe('SystemView', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('shows the app version, API versions and health details', async () => {
    const fake = server().on('GET', '/health', 503, { status: 'unhealthy', checks: { database: { status: 'unhealthy', description: 'database is locked', warnings: null } } })
    const wrapper = await mountSystem(fake)

    expect(wrapper.get('[data-test="version"]').text()).toBe('0.1.0+abc')
    expect(wrapper.get('[data-test="api-versions"]').text()).toContain('1.0')
    const database = wrapper.get('[data-check="database"]').text()
    expect(database).toContain('Database')
    expect(database).toContain('unhealthy')
    expect(database).toContain('database is locked')
    expect(wrapper.get('[data-test="overall"]').text()).toContain('unhealthy')
  })

  it('lists the event sources with their state and the last webhook time', async () => {
    const wrapper = await mountSystem(server())

    expect(wrapper.findAll('[data-source]').map((row) => row.attributes('data-source'))).toEqual(['ha', 'mail', 'uptime-kuma'])
    expect(wrapper.get('[data-source="ha"]').text()).toContain('Home Assistant')
    expect(wrapper.get('[data-source="mail"]').text()).toContain('not connected')
    const kuma = wrapper.get('[data-source="uptime-kuma"]').text()
    expect(kuma).toContain('Uptime Kuma')
    expect(kuma).toContain('Last webhook')
    expect(kuma).toContain('10:15')
  })

  it('shows the event sources in German', async () => {
    const wrapper = await mountSystem(server(), 'de')

    expect(wrapper.text()).toContain('Ereignisquellen')
    expect(wrapper.get('[data-source="mail"]').text()).toContain('nicht verbunden')
    expect(wrapper.get('[data-source="uptime-kuma"]').text()).toContain('Letzter Webhook')
    expect(wrapper.get('[data-source="uptime-kuma"]').text()).toContain('10:15')
  })

  it('lists warnings of a degraded check', async () => {
    const wrapper = await mountSystem(server().on('GET', '/health', 200, health({ status: 'degraded', warnings: ['a (b)'] })))

    expect(wrapper.get('[data-check="secrets"]').text()).toContain('a (b)')
  })

  it('shows deprecated API versions', async () => {
    const wrapper = await mountSystem(server().on('GET', '/api/v1/system', 200, { ...info, supportedApiVersions: ['1.0', '2.0'], deprecatedApiVersions: ['1.0'] }))

    expect(wrapper.get('[data-test="deprecated"]').text()).toContain('1.0')
  })

  it('offers the kill switch with the number of running tasks', async () => {
    const fake = server().on('GET', '/api/v1/tasks', 200, [
      { id: 'a', status: 'running' },
      { id: 'b', status: 'pending' },
    ])
    const wrapper = await mountSystem(fake)

    expect(wrapper.get('[data-test="kill-switch"]').text()).toContain('1 task running')
  })

  it('links to the configuration for admins only', async () => {
    const admin = await mountSystem(server({ ...me, roles: ['admin'] }))
    expect(admin.find('a[href="/system/config"]').exists()).toBe(true)
    admin.unmount()

    const user = await mountSystem(server({ ...me, roles: [] }))
    expect(user.find('a[href="/system/config"]').exists()).toBe(false)
  })

  it('shows an error when the system information fails', async () => {
    const wrapper = await mountSystem(server().on('GET', '/api/v1/system', 500, { status: 500 }))

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })

  it('speaks German', async () => {
    const wrapper = await mountSystem(server(), 'de')

    expect(wrapper.get('[data-check="database"]').text()).toContain('Datenbank')
    expect(wrapper.get('[data-check="database"]').text()).toContain('in Ordnung')
  })
})
