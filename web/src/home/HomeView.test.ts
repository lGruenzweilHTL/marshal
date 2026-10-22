import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import HomeView from './HomeView.vue'
import { FakeServer } from '../testing/fakeServer'
import { approvalItem, health, maintenanceItem, notificationEntry, printerState, printerSummary, runSummary, taskItem } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

const future = new Date(Date.now() + 5 * 60_000).toISOString()
const cost = {
  timeZone: 'Europe/Vienna',
  from: '2026-09-15',
  to: '2026-09-28',
  days: [
    { date: '2026-09-27', total: 0.1, byTier: { fast: 0.1 }, advisor: 0 },
    { date: '2026-09-28', total: 0.42, byTier: { fast: 0.4 }, advisor: 0.02 },
  ],
  total: 0.52,
  byTier: { fast: 0.5 },
  advisor: 0.02,
}

function server() {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .antiforgery('xsrf')
    .on('GET', '/health', 200, health())
    .on('GET', '/api/v1/approvals', 200, [approvalItem({ expiresAt: future })])
    .on('GET', '/api/v1/printers', 200, [printerSummary()])
    .on('GET', '/api/v1/tasks', 200, [taskItem({ id: 't-run', name: 'babysitter', status: 'running' }), taskItem()])
    .on('GET', '/api/v1/tasks/kill-switch', 200, { active: false })
    .on('GET', '/api/v1/notifications', 200, [notificationEntry()])
    .on('GET', '/api/v1/runs', 200, [runSummary()])
    .on('GET', '/api/v1/cost', 200, cost)
    .on('GET', '/api/v1/maintenance', 200, [])
}

async function mountHome(fake: FakeServer) {
  const mounted = await mountOptions(fake, '/')
  const wrapper = mount(HomeView, { ...mounted.options, attachTo: document.body })
  await flushPromises()
  return { ...mounted, wrapper }
}

const sections = (wrapper: { findAll(selector: string): { attributes(name: string): string | undefined }[] }) =>
  wrapper.findAll('[data-section]').map((section) => section.attributes('data-section'))

describe('HomeView', () => {
  it('orders the phone layout by what needs the user', async () => {
    useViewport('phone')
    const { wrapper } = await mountHome(server())

    expect(sections(wrapper)).toEqual(['approvals', 'printer', 'tasks', 'notifications', 'runs', 'cost'])
    expect(wrapper.find('[data-approval="a-1"] [data-test="approve"]').exists()).toBe(true)
    wrapper.unmount()
  })

  it('leaves out the approvals when none is pending', async () => {
    useViewport('phone')
    const { wrapper } = await mountHome(server().on('GET', '/api/v1/approvals', 200, []))

    expect(sections(wrapper)[0]).toBe('printer')
    wrapper.unmount()
  })

  it('shows the kill switch only while a task runs', async () => {
    useViewport('phone')
    const { wrapper } = await mountHome(server())
    expect(wrapper.find('[data-test="kill-switch"]').exists()).toBe(true)
    wrapper.unmount()

    const idle = await mountHome(server().on('GET', '/api/v1/tasks', 200, [taskItem()]))
    expect(idle.wrapper.find('[data-test="kill-switch"]').exists()).toBe(false)
    idle.wrapper.unmount()
  })

  it('stops background work after confirmation', async () => {
    useViewport('phone')
    const fake = server().on('POST', '/api/v1/tasks/kill-switch/activate', 200, { active: true, cancelled: 1 })
    const { wrapper } = await mountHome(fake)

    await wrapper.get('[data-test="kill-switch"] [data-test="stop"]').trigger('click')
    expect(fake.count('POST', '/api/v1/tasks/kill-switch/activate')).toBe(0)
    ;(document.body.querySelector('[data-test="confirm-stop"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(fake.count('POST', '/api/v1/tasks/kill-switch/activate')).toBe(1)
    expect(wrapper.get('[data-test="kill-switch"]').text()).toContain('1 cancelled')
    wrapper.unmount()
  })

  it('shows running tasks and the next scheduled runs', async () => {
    useViewport('phone')
    const { wrapper, router } = await mountHome(server())

    const tasks = wrapper.get('[data-section="tasks"]')
    expect(tasks.text()).toContain('babysitter')
    expect(tasks.text()).toContain('running')
    expect(tasks.text()).toContain('briefing')
    wrapper.unmount()
  })

  it('shows today’s cost with the advisor share', async () => {
    useViewport('phone')
    const { wrapper } = await mountHome(server())

    const card = wrapper.get('[data-section="cost"]')
    expect(card.text()).toContain('0.42')
    expect(card.text()).toContain('0.02')
    wrapper.unmount()
  })

  it('warns about health problems and links to the system view', async () => {
    useViewport('phone')
    const fake = server().on('GET', '/health', 200, health({ status: 'degraded', warnings: ['printers[0].apiKeySecret (moonraker_voron)'] }))
    const { wrapper } = await mountHome(fake)

    const strip = wrapper.get('[data-test="health"]')
    expect(strip.text()).toContain('moonraker_voron')
    expect(strip.attributes('href')).toBe('/system')
    wrapper.unmount()
  })

  it('says nothing about health while everything is fine', async () => {
    useViewport('phone')
    const { wrapper } = await mountHome(server())

    expect(wrapper.find('[data-test="health"]').exists()).toBe(false)
    wrapper.unmount()
  })

  it('keeps the other widgets when one fails', async () => {
    useViewport('phone')
    const { wrapper } = await mountHome(server().on('GET', '/api/v1/runs', 500, { status: 500, code: 'server.unexpected' }))

    expect(wrapper.get('[data-section="runs"]').text()).toContain('Try again')
    expect(wrapper.get('[data-section="cost"]').text()).toContain('0.42')
    wrapper.unmount()
  })

  it('puts a printer alert first in the printer section', async () => {
    useViewport('phone')
    const fake = server().on('GET', '/api/v1/printers', 200, [
      printerSummary({ name: 'prusa' }),
      printerSummary({ name: 'voron', state: printerState({ printState: 'paused', message: 'Filament runout' }) }),
    ])
    const { wrapper } = await mountHome(fake)

    expect(wrapper.findAll('[data-printer]').map((card) => card.attributes('data-printer'))).toEqual(['voron', 'prusa'])
    wrapper.unmount()
  })

  it('shows maintenance after the printers, items that are due or soon first, and hides it without items', async () => {
    useViewport('phone')
    const fake = server().on('GET', '/api/v1/maintenance', 200, [
      maintenanceItem({ id: 'dishwasher_filter', name: 'Dishwasher filter', usage: 61, every: 60, warnBefore: null, unit: null, state: 'due' }),
      maintenanceItem(),
      maintenanceItem({ id: 'belts', name: 'Belts', usage: 12, every: 500, state: 'ok' }),
    ])
    const { wrapper } = await mountHome(fake)

    expect(sections(wrapper).slice(0, 3)).toEqual(['approvals', 'printer', 'maintenance'])
    expect(wrapper.findAll('[data-maintenance]').map((row) => row.attributes('data-maintenance'))).toEqual(['dishwasher_filter', 'voron_nozzle', 'belts'])
    expect(wrapper.get('[data-maintenance="voron_nozzle"]').text()).toContain('182.5 / 200 h')
    wrapper.unmount()

    const none = await mountHome(server())
    expect(sections(none.wrapper)).not.toContain('maintenance')
    none.wrapper.unmount()
  })

  it('marks a maintenance item done only after confirmation', async () => {
    useViewport('phone')
    const fake = server()
      .on('GET', '/api/v1/maintenance', 200, [maintenanceItem()])
      .on('POST', '/api/v1/maintenance/voron_nozzle/done', 200, maintenanceItem({ usage: 0, state: 'ok' }))
    const { wrapper, toasts } = await mountHome(fake)

    await wrapper.get('[data-maintenance="voron_nozzle"] [data-test="done"]').trigger('click')
    expect(fake.count('POST', '/api/v1/maintenance/voron_nozzle/done')).toBe(0)
    expect(document.body.textContent).toContain('Mark Voron nozzle as done?')
    fake.on('GET', '/api/v1/maintenance', 200, [maintenanceItem({ usage: 0, state: 'ok' })])
    ;(document.body.querySelector('[data-test="confirm-done"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(fake.count('POST', '/api/v1/maintenance/voron_nozzle/done')).toBe(1)
    expect(wrapper.get('[data-maintenance="voron_nozzle"]').text()).toContain('0 / 200 h')
    expect(toasts.items.value.map((toast) => toast.message)).toContain('Voron nozzle marked done. Counting starts again.')
    wrapper.unmount()
  })
})
