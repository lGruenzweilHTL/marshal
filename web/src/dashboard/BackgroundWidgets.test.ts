import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import DashboardView from './DashboardView.vue'
import { FakeServer } from '../testing/fakeServer'
import { approvalItem, health, notificationEntry, taskItem } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server() {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .antiforgery('xsrf')
    .on('GET', '/api/v1/runs', 200, [])
    .on('GET', '/api/v1/cost', 200, { timeZone: 'Europe/Vienna', from: '2026-09-30', to: '2026-09-30', days: [], total: 0, byTier: {}, advisor: 0 })
    .on('GET', '/health', 200, health())
    .on('GET', '/api/v1/printers', 200, [])
    .on('GET', '/api/v1/tasks', 200, [
      taskItem({ id: 't-run', name: 'research', goal: 'Research heat pumps.', status: 'running', nextRunAt: null }),
      taskItem({ id: 't-late', name: 'late', nextRunAt: '2026-10-02T04:00:00Z' }),
      taskItem({ id: 't-soon', name: 'briefing', nextRunAt: '2026-10-01T04:00:00Z' }),
    ])
    .on('GET', '/api/v1/approvals', 200, [approvalItem()])
    .on('GET', '/api/v1/notifications', 200, [notificationEntry({ content: 'Washer done.' })])
    .on('GET', '/api/v1/tasks/kill-switch', 200, { active: false })
}

async function mountDashboard(fake: FakeServer) {
  const mounted = await mountOptions(fake, '/')
  const wrapper = mount(DashboardView, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('background widgets', () => {
  it('shows running tasks and the next scheduled runs, soonest first', async () => {
    const { wrapper } = await mountDashboard(server())

    const widget = wrapper.get('[data-widget="tasks"]')
    expect(widget.get('[data-test="running"]').text()).toContain('research')
    expect(widget.findAll('[data-test="next"] li').map((item) => item.text().split(' ')[0])).toEqual(['briefing', 'late'])
  })

  it('shows pending approvals with approve and reject', async () => {
    const fake = server().on('POST', '/api/v1/approvals/a-1/approve', 200, { id: 'a-1', status: 'approved' })
    const { wrapper } = await mountDashboard(fake)

    const widget = wrapper.get('[data-widget="approvals"]')
    expect(widget.text()).toContain('Unlock the front door')
    expect(widget.find('[data-test="reject"]').exists()).toBe(true)
    await widget.get('[data-test="approve"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/approvals/a-1/approve')).toBe(1)
    expect(wrapper.get('[data-widget="approvals"]').text()).not.toContain('Unlock the front door')
  })

  it('shows recent notifications', async () => {
    const fake = server()
    const { wrapper } = await mountDashboard(fake)

    expect(wrapper.get('[data-widget="notifications"]').text()).toContain('Washer done.')
    expect(new URLSearchParams(fake.request('GET', '/api/v1/notifications')!.search).get('limit')).toBe('5')
  })

  it('has the kill switch', async () => {
    const { wrapper } = await mountDashboard(server())

    expect(wrapper.get('[data-widget="kill-switch"] [data-test="kill-switch"]').text()).toContain('running normally')
  })
})
