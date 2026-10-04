import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrinterCard from './PrinterCard.vue'
import { printerStatus } from './printerStatus'
import { FakeServer } from '../testing/fakeServer'
import { printerState, printerSummary } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server() {
  return new FakeServer().on('GET', '/api/v1/me', 200, me).antiforgery('xsrf')
}

async function mountCard(fake: FakeServer, props: InstanceType<typeof PrinterCard>['$props']) {
  const mounted = await mountOptions(fake, '/')
  const wrapper = mount(PrinterCard, { ...mounted.options, props, attachTo: document.body })
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('printerStatus', () => {
  it('maps Moonraker and Klipper states to what the card shows', () => {
    expect(printerStatus(printerSummary())).toBe('printing')
    expect(printerStatus(printerSummary({ online: false, state: null }))).toBe('offline')
    expect(printerStatus(printerSummary({ state: printerState({ printState: 'paused' }) }))).toBe('paused')
    expect(printerStatus(printerSummary({ state: printerState({ printState: 'error' }) }))).toBe('error')
    expect(printerStatus(printerSummary({ state: printerState({ klippyState: 'shutdown', printState: 'standby' }) }))).toBe('error')
    expect(printerStatus(printerSummary({ state: printerState({ printState: 'complete' }) }))).toBe('complete')
    expect(printerStatus(printerSummary({ state: printerState({ printState: 'standby' }) }))).toBe('idle')
  })
})

describe('PrinterCard', () => {
  it('shows progress, layer, time left and temperatures of a running print', async () => {
    const { wrapper } = await mountCard(server(), { printer: printerSummary() })

    expect(wrapper.text()).toContain('voron')
    expect(wrapper.text()).toContain('benchy.gcode')
    expect(wrapper.text()).toContain('printing')
    expect(wrapper.get('[data-test="progress"]').text()).toBe('25%')
    expect(wrapper.text()).toContain('Layer 30 / 120')
    expect(wrapper.text()).toContain('30 min left')
    expect(wrapper.text()).toContain('200 / 245 °C')
  })

  it('compact density leaves out temperatures and actions', async () => {
    const { wrapper } = await mountCard(server(), { printer: printerSummary(), density: 'compact', actions: true })

    expect(wrapper.text()).not.toContain('°C')
    expect(wrapper.find('[data-control="pause"]').exists()).toBe(false)
  })

  it('explains an offline printer', async () => {
    const { wrapper } = await mountCard(server(), { printer: printerSummary({ online: false, state: null }) })

    expect(wrapper.text()).toContain('offline')
    expect(wrapper.text()).toContain('not reachable')
  })

  it('shows the error message of a failed print as an alert', async () => {
    const { wrapper } = await mountCard(server(), { printer: printerSummary({ state: printerState({ printState: 'error', message: 'Heater extruder not heating at expected rate' }) }) })

    expect(wrapper.get('[role="alert"]').text()).toContain('Heater extruder not heating')
  })

  it('pauses right away and asks before cancelling', async () => {
    const fake = server()
    fake.handle('POST', '/api/v1/printers/voron/controls', async (request) => {
      const body = JSON.parse(request.body) as { action: string }
      return body.action === 'cancel'
        ? new Response(JSON.stringify({ status: 'confirmation_required', description: 'Cancel the print on voron', token: 'tok' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        : new Response(JSON.stringify({ status: 'done', description: 'Paused' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
    fake.on('POST', '/api/v1/printers/voron/controls/confirm', 200, { status: 'done', description: 'Cancelled' })
    const { wrapper } = await mountCard(fake, { printer: printerSummary(), actions: true })

    await wrapper.get('[data-control="pause"]').trigger('click')
    await flushPromises()
    expect(fake.count('POST', '/api/v1/printers/voron/controls')).toBe(1)

    await wrapper.get('[data-control="cancel"]').trigger('click')
    await flushPromises()
    expect(document.body.textContent).toContain('Cancel the print on voron')

    ;(document.body.querySelector('[data-test="confirm"]') as HTMLButtonElement).click()
    await flushPromises()
    expect(fake.request('POST', '/api/v1/printers/voron/controls/confirm')?.body).toBe(JSON.stringify({ token: 'tok' }))
    wrapper.unmount()
  })

  it('keeps a refused command on screen', async () => {
    const fake = server().on('POST', '/api/v1/printers/voron/controls', 409, { status: 409, code: 'printer.offline', detail: 'The printer is offline.' })
    const { wrapper } = await mountCard(fake, { printer: printerSummary(), actions: true })

    await wrapper.get('[data-control="pause"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('The printer is offline.')
    wrapper.unmount()
  })
})
