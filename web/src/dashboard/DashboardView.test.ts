import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import DashboardView from './DashboardView.vue'
import { FakeServer } from '../testing/fakeServer'
import { health, runSummary } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server(healthBody = health()) {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .on('GET', '/api/v1/runs', 200, [runSummary({ id: 'r-2', trigger: 'conversation' }), runSummary({ id: 'r-1', trigger: 'task' })])
    .on('GET', '/api/v1/cost', 200, {
      timeZone: 'Europe/Vienna',
      from: '2026-09-28',
      to: '2026-09-28',
      days: [{ date: '2026-09-28', total: 0.42, byTier: { fast: 0.42 }, advisor: 0 }],
      total: 0.42,
      byTier: { fast: 0.42 },
      advisor: 0,
    })
    .on('GET', '/health', 200, healthBody)
    .on('GET', '/api/v1/printers', 200, [])
}

async function mountDashboard(fake: FakeServer) {
  const { options } = await mountOptions(fake, '/')
  const wrapper = mount(DashboardView, options)
  await flushPromises()
  return wrapper
}

describe('DashboardView', () => {
  it('shows recent runs, today’s cost and health', async () => {
    const fake = server()
    const wrapper = await mountDashboard(fake)

    expect(wrapper.get('[data-widget="recent-runs"]').findAll('li')).toHaveLength(2)
    expect(new URLSearchParams(fake.request('GET', '/api/v1/runs')!.search).get('limit')).toBe('5')
    expect(wrapper.get('[data-widget="today-cost"]').text()).toContain('0.42')
    expect(wrapper.get('[data-widget="health"]').text()).toContain('healthy')
  })

  it('shows a health warning', async () => {
    const wrapper = await mountDashboard(server(health({ status: 'degraded', warnings: ['printers[0].apiKeySecret (moonraker_voron)'] })))

    const widget = wrapper.get('[data-widget="health"]')
    expect(widget.text()).toContain('degraded')
    expect(widget.text()).toContain('printers[0].apiKeySecret (moonraker_voron)')
  })
})
