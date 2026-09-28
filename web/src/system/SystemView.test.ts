import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SystemView from './SystemView.vue'
import { FakeServer } from '../testing/fakeServer'
import { health } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

describe('SystemView', () => {
  it('shows the app version, API versions and health details', async () => {
    const fake = new FakeServer()
      .on('GET', '/api/v1/me', 200, me)
      .on('GET', '/api/v1/system', 200, { version: '0.1.0+abc', supportedApiVersions: ['1.0'], deprecatedApiVersions: [] })
      .on('GET', '/health', 503, { status: 'unhealthy', checks: { database: { status: 'unhealthy', description: 'database is locked', warnings: null } } })
    const { options } = await mountOptions(fake, '/system')
    const wrapper = mount(SystemView, options)
    await flushPromises()

    expect(wrapper.get('[data-test="version"]').text()).toBe('0.1.0+abc')
    expect(wrapper.get('[data-test="api-versions"]').text()).toContain('1.0')
    const database = wrapper.get('[data-check="database"]').text()
    expect(database).toContain('unhealthy')
    expect(database).toContain('database is locked')
  })

  it('lists warnings of a degraded check', async () => {
    const fake = new FakeServer()
      .on('GET', '/api/v1/me', 200, me)
      .on('GET', '/api/v1/system', 200, { version: '0.1.0', supportedApiVersions: ['1.0'], deprecatedApiVersions: [] })
      .on('GET', '/health', 200, health({ status: 'degraded', warnings: ['a (b)'] }))
    const { options } = await mountOptions(fake, '/system')
    const wrapper = mount(SystemView, options)
    await flushPromises()

    expect(wrapper.get('[data-check="secrets"]').text()).toContain('a (b)')
  })
})
