import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import PrinterWidget from './PrinterWidget.vue'
import { FakeServer } from '../testing/fakeServer'
import { printerState, printerSummary } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

async function mountWidget(fake: FakeServer) {
  const mounted = await mountOptions(fake, '/')
  const wrapper = mount(PrinterWidget, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('PrinterWidget', () => {
  it('shows each printer’s job and follows live updates', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/printers', 200, [printerSummary(), printerSummary({ name: 'prusa', online: false, state: null })])
    const { wrapper, live } = await mountWidget(fake)

    expect(wrapper.text()).toContain('voron')
    expect(wrapper.text()).toContain('25%')
    expect(wrapper.text()).toContain('prusa')
    expect(wrapper.text()).toContain('offline')

    live.publish('printer', 'printer.state', { printer: 'voron', state: printerState({ progress: 0.5 }) })
    await flushPromises()
    expect(wrapper.text()).toContain('50%')
  })

  it('renders nothing without printers', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/printers', 200, [])
    const { wrapper } = await mountWidget(fake)

    expect(wrapper.text()).toContain('No printer')
  })
})
