import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
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

const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')

afterEach(() => {
  document.body.innerHTML = ''
})

describe('PrinterView', () => {
  it('shows live status: job, progress, time left, layer and temperatures', async () => {
    const { wrapper } = await mountPrinter(server())

    const status = wrapper.get('[data-printer="voron"]').text()
    expect(status).toContain('printing')
    expect(status).toContain('benchy.gcode')
    expect(status).toContain('25%')
    expect(status).toContain('30 min left')
    expect(status).toContain('Layer 30 / 120')
    expect(wrapper.get('[data-test="temperatures"]').text()).toContain('200 / 245 °C')
    expect(wrapper.get('[role="progressbar"]').attributes('aria-valuenow')).toBe('25')
  })

  it('updates from the printer live topic and charts the temperatures', async () => {
    const { wrapper, live } = await mountPrinter(server())

    live.publish('printer', 'printer.state', { printer: 'voron', state: printerState({ extruder: { temperature: 230, target: 245, power: 1 } }) })
    await flushPromises()

    expect(wrapper.get('[data-test="temperatures"]').text()).toContain('230 / 245 °C')
    expect(wrapper.findAll('[data-test="chart"] path').length).toBeGreaterThan(0)

    live.publish('printer', 'printer.state', { printer: 'voron', state: null })
    await flushPromises()
    expect(wrapper.get('[data-printer="voron"]').text()).toContain('offline')
  })

  it('shows the babysitter with its last checks and their snapshots', async () => {
    const babysitter = {
      active: true,
      verdicts: [
        { at: '2026-09-30T10:00:00Z', outcome: 'ok', reason: 'looks fine', snapshotUrl: `${base}/snapshots/s1` },
        { at: '2026-09-30T10:01:00Z', outcome: 'failure', reason: 'spaghetti', snapshotUrl: null },
      ],
      pausedAt: null,
    }
    const { wrapper } = await mountPrinter(server(printerSummary({ babysitter })))

    const card = wrapper.get('[data-test="babysitter"]').text()
    expect(card).toContain('Watching this print')
    expect(card).toContain('looks fine')
    expect(card).toContain('spaghetti')
    expect(wrapper.get('[data-test="babysitter"] a').attributes('href')).toBe(`${base}/snapshots/s1`)
  })

  it('hides the babysitter when it is not enabled', async () => {
    const { wrapper } = await mountPrinter(server())

    expect(wrapper.find('[data-test="babysitter"]').exists()).toBe(false)
  })

  it('loads the webcam stream from Marshal, not from the printer', async () => {
    const { wrapper } = await mountPrinter(server())

    expect(wrapper.get('[data-test="webcam"]').attributes('src')).toBe(`https://marshal.test${base}/webcam/stream`)
  })

  it('keeps the shown snapshot as an artifact with a toast', async () => {
    const fake = server()
      .on('POST', `${base}/snapshots`, 200, { id: 'snap1', url: `${base}/snapshots/snap1`, width: 1024, height: 768, capturedAt: '2026-09-29T10:00:00Z' })
      .on('POST', `${base}/snapshots/snap1/keep`, 201, { id: 'a1', name: 'voron-snap1.jpg', kind: 'image', mediaType: 'image/jpeg', size: 4, source: 'printer', sourceReference: 'printer:voron/snapshot:snap1', tainted: false, createdAt: '2026-09-29T10:00:01Z', url: '/api/v1/artifacts/a1/content' })
    const { wrapper, toasts } = await mountPrinter(fake)
    expect(wrapper.find('[data-test="keep"]').exists()).toBe(false)

    await wrapper.findAll('button').find((button) => button.text() === 'Snapshot')!.trigger('click')
    await flushPromises()
    await wrapper.get('[data-test="keep"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', `${base}/snapshots/snap1/keep`)).toBe(1)
    expect(toasts.items.value.map((toast) => toast.message)).toEqual(['Snapshot kept in Artifacts.'])
    expect(wrapper.get('[data-test="open-kept"]').attributes('href')).toBe('/artifacts/a1')
    expect(wrapper.get('[data-test="keep"]').attributes('disabled')).toBeDefined()
  })

  it('pauses immediately without a dialog', async () => {
    const fake = server().on('POST', `${base}/controls`, 200, { status: 'done', description: 'pause the print' })
    const { wrapper } = await mountPrinter(fake)

    await wrapper.get('[data-control="pause"]').trigger('click')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls`)!.body)).toEqual({ action: 'pause' })
    expect(dialog()).toBeNull()
  })

  it('starts a print only after the confirmation dialog', async () => {
    const fake = server()
      .on('POST', `${base}/controls`, 200, { status: 'confirmation_required', description: 'start printing benchy.gcode', token: 'tok-1', expiresAt: '2026-09-29T10:02:00Z' })
      .on('POST', `${base}/controls/confirm`, 200, { status: 'done', description: 'start printing benchy.gcode' })
    const { wrapper } = await mountPrinter(fake)

    await wrapper.get('[data-file="benchy.gcode"] [data-control="print"]').trigger('click')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls`)!.body)).toEqual({ action: 'start_print', file: 'benchy.gcode' })
    expect(dialog()?.textContent).toContain('start printing benchy.gcode')
    expect(fake.count('POST', `${base}/controls/confirm`)).toBe(0)

    dialog()!.querySelector<HTMLButtonElement>('[data-test="confirm"]')!.click()
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls/confirm`)!.body)).toEqual({ token: 'tok-1' })
    expect(dialog()).toBeNull()
  })

  it('cancelling the dialog sends nothing', async () => {
    const fake = server().on('POST', `${base}/controls`, 200, { status: 'confirmation_required', description: 'set the bed to 60 °C', token: 'tok-2' })
    const { wrapper } = await mountPrinter(fake)

    await wrapper.get('input[name="bedTarget"]').setValue('60')
    await wrapper.get('[data-control="bed"]').trigger('click')
    await flushPromises()
    dialog()!.querySelector<HTMLButtonElement>('[data-test="dismiss"]')!.click()
    await flushPromises()

    expect(JSON.parse(fake.request('POST', `${base}/controls`)!.body)).toEqual({ action: 'set_temperature', heater: 'bed', target: 60 })
    expect(fake.count('POST', `${base}/controls/confirm`)).toBe(0)
    expect(dialog()).toBeNull()
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

  it('shows a failed print as an alert with resume and cancel (the alert deep link)', async () => {
    const failed = printerSummary({ state: printerState({ printState: 'paused', message: 'Spaghetti detected at layer 112.' }) })
    const { wrapper } = await mountPrinter(server(failed), '/printers/voron')

    expect(wrapper.get('[data-printer="voron"] [role="alert"]').text()).toContain('Spaghetti detected')
    expect(wrapper.find('[data-printer="voron"] [data-control="resume"]').exists()).toBe(true)
    expect(wrapper.find('[data-printer="voron"] [data-control="cancel"]').exists()).toBe(true)
  })

  it('switches between printers by route', async () => {
    const fake = server()
      .on('GET', '/api/v1/printers', 200, [printerSummary(), printerSummary({ name: 'prusa' })])
      .on('GET', '/api/v1/printers/prusa/files', 200, [])
      .on('GET', '/api/v1/printers/prusa/queue', 200, { state: 'ready', jobs: [] })
      .on('GET', '/api/v1/printers/prusa/history', 200, [])
    const { wrapper, router } = await mountPrinter(fake)

    await wrapper.get('[data-test="printers"] [data-value="prusa"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.fullPath).toBe('/printers/prusa')
    expect(wrapper.find('[data-printer="prusa"]').exists()).toBe(true)
    expect(fake.count('GET', '/api/v1/printers/prusa/files')).toBe(1)
  })

  it('keeps the page when a detail list fails', async () => {
    const { wrapper } = await mountPrinter(server().on('GET', `${base}/history`, 500, { status: 500, code: 'server.unexpected' }))

    expect(wrapper.get('[data-test="history"]').text()).toContain('Try again')
    expect(wrapper.get('[data-test="queue"]').text()).toContain('a.gcode')
  })
})
