import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrinterView from './PrinterView.vue'
import { FakeServer } from '../testing/fakeServer'
import { printerState, printerSummary } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

const base = '/api/v1/printers/voron'

function server(summary = printerSummary()) {
  return new FakeServer()
    .antiforgery('csrf')
    .on('GET', '/api/v1/me', 200, me)
    .on('GET', '/api/v1/printers', 200, [summary])
    .on('GET', `${base}/files`, 200, [{ path: 'benchy.gcode', modified: '2026-09-28T10:00:00Z', size: 2048 }])
    .on('GET', `${base}/queue`, 200, { state: 'ready', jobs: [{ jobId: '0001', filename: 'a.gcode', addedAt: '2026-09-28T10:00:00Z' }] })
    .on('GET', `${base}/history`, 200, [
      { jobId: '0002', filename: 'b.gcode', status: 'completed', startedAt: '2026-09-28T08:00:00Z', endedAt: '2026-09-28T09:00:00Z', printDuration: 3500, totalDuration: 3600, filamentUsed: 1000, exists: true },
    ])
}

async function mountPrinter(fake: FakeServer, path = '/printers') {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(PrinterView, { ...mounted.options, attachTo: document.body })
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('PrinterView', () => {
  it('shows live status: job, progress, time left, layer and temperatures', async () => {
    const { wrapper } = await mountPrinter(server())

    const status = wrapper.get('[data-test="status"]').text()
    expect(status).toContain('printing')
    expect(status).toContain('benchy.gcode')
    expect(status).toContain('25%')
    expect(status).toContain('30 min left')
    expect(status).toContain('Layer 30 / 120')
    expect(wrapper.get('[data-test="temperatures"]').text()).toContain('200 / 245 °C')
    expect(wrapper.get('progress').attributes('value')).toBe('0.25')
  })

  it('updates from the printer live topic and charts the temperatures', async () => {
    const { wrapper, live } = await mountPrinter(server())

    live.publish('printer', 'printer.state', { printer: 'voron', state: printerState({ extruder: { temperature: 230, target: 245, power: 1 } }) })
    await flushPromises()

    expect(wrapper.get('[data-test="temperatures"]').text()).toContain('230 / 245 °C')
    expect(wrapper.findAll('[data-test="chart"] polyline').length).toBeGreaterThan(0)

    live.publish('printer', 'printer.state', { printer: 'voron', state: null })
    await flushPromises()
    expect(wrapper.get('[data-test="status"]').text()).toContain('offline')
  })

  it('loads the webcam stream from Marshal, not from the printer', async () => {
    const { wrapper } = await mountPrinter(server())

    expect(wrapper.get('[data-test="webcam"]').attributes('src')).toBe(`https://marshal.test${base}/webcam/stream`)
  })

  it('pauses immediately without a dialog', async () => {
    const fake = server().on('POST', `${base}/controls`, 200, { status: 'done', description: 'pause the print' })
    const { wrapper } = await mountPrinter(fake)

    await wrapper.get('[data-control="pause"]').trigger('click')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls`)!.body)).toEqual({ action: 'pause' })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('starts a print only after the confirmation dialog', async () => {
    const fake = server()
      .on('POST', `${base}/controls`, 200, { status: 'confirmation_required', description: 'start printing benchy.gcode', token: 'tok-1', expiresAt: '2026-09-29T10:02:00Z' })
      .on('POST', `${base}/controls/confirm`, 200, { status: 'done', description: 'start printing benchy.gcode' })
    const { wrapper } = await mountPrinter(fake)

    await wrapper.get('[data-file="benchy.gcode"] button').trigger('click')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls`)!.body)).toEqual({ action: 'start_print', file: 'benchy.gcode' })
    const dialog = wrapper.get('[role="dialog"]')
    expect(dialog.text()).toContain('start printing benchy.gcode')
    expect(fake.count('POST', `${base}/controls/confirm`)).toBe(0)

    await dialog.get('[data-test="confirm"]').trigger('click')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls/confirm`)!.body)).toEqual({ token: 'tok-1' })
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('cancelling the dialog sends nothing', async () => {
    const fake = server().on('POST', `${base}/controls`, 200, { status: 'confirmation_required', description: 'set the bed to 60 °C', token: 'tok-2' })
    const { wrapper } = await mountPrinter(fake)

    await wrapper.get('input[name="bedTarget"]').setValue('60')
    await wrapper.get('[data-control="bed"]').trigger('click')
    await flushPromises()
    await wrapper.get('[role="dialog"] [data-test="cancel"]').trigger('click')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls`)!.body)).toEqual({ action: 'set_temperature', heater: 'bed', target: 60 })
    expect(fake.count('POST', `${base}/controls/confirm`)).toBe(0)
    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })

  it('offers only allowlisted macros and links to the printer UI for everything else', async () => {
    const { wrapper } = await mountPrinter(server())

    expect(wrapper.findAll('[data-control="macro"]').map((button) => button.text())).toEqual(['PARK'])
    expect(wrapper.find('textarea').exists()).toBe(false)
    expect(wrapper.get('a[data-test="web-ui"]').attributes('href')).toBe('http://voron.local/')
  })

  it('shows the job queue and print history', async () => {
    const { wrapper } = await mountPrinter(server())

    expect(wrapper.get('[data-test="queue"]').text()).toContain('a.gcode')
    expect(wrapper.get('[data-test="history"]').text()).toContain('b.gcode')
    expect(wrapper.get('[data-test="history"]').text()).toContain('completed')
  })

  it('shows a refused control as an error', async () => {
    const fake = server().on('POST', `${base}/controls`, 400, { title: 'printer.macro_not_allowed', detail: 'The macro is not allowed.', code: 'printer.macro_not_allowed' })
    const { wrapper } = await mountPrinter(fake)

    await wrapper.get('[data-control="macro"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('The macro is not allowed.')
  })

  it('says when no printer is configured', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/printers', 200, [])
    const { wrapper } = await mountPrinter(fake)

    expect(wrapper.text()).toContain('No printer is configured')
  })
})
