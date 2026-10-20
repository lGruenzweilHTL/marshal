import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it, vi } from 'vitest'
import ChatWidget from './ChatWidget.vue'
import { FakeServer } from '../../testing/fakeServer'
import { printerState } from '../../testing/fixtures'
import { me, mountOptions } from '../../testing/mountApp'
import type { TurnWidget } from '../turnEvents'

function server() {
  return new FakeServer().on('GET', '/api/v1/me', 200, me).antiforgery('xsrf')
}

async function mountWidget(fake: FakeServer, widget: TurnWidget, language: 'en' | 'de' = 'en') {
  const mounted = await mountOptions(fake, '/chat', { language })
  const wrapper = mount(ChatWidget, { ...mounted.options, props: { widget }, attachTo: document.body })
  await flushPromises()
  return { ...mounted, wrapper }
}

const printer: TurnWidget = { kind: 'printer', toolCallId: 'c-1', data: { printer: 'voron', state: printerState() } }
const musicData = { speaker: 'kitchen', state: 'playing', current: { name: 'Innuendo', artists: ['Queen'], album: 'Innuendo' }, next: { name: 'Headlong', artists: ['Queen'], album: null }, items: 12, shuffle: false, repeat: null }
const music: TurnWidget = { kind: 'music', toolCallId: 'c-2', data: musicData }

describe('ChatWidget', () => {
  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('renders the printer widget with the current state and follows live updates', async () => {
    const fake = server().on('GET', '/api/v1/printers/voron', 200, printerState({ progress: 0.3 }))
    const { wrapper, live } = await mountWidget(fake, printer)

    expect(wrapper.get('[data-widget="printer"]').text()).toContain('voron')
    expect(wrapper.get('[data-test="progress"]').text()).toBe('30%')

    live.publish('printer', 'printer.state', { printer: 'voron', state: printerState({ progress: 0.5 }) })
    await flushPromises()
    expect(wrapper.get('[data-test="progress"]').text()).toBe('50%')
    live.publish('printer', 'printer.state', { printer: 'prusa', state: printerState({ progress: 0.9 }) })
    await flushPromises()
    expect(wrapper.get('[data-test="progress"]').text()).toBe('50%')
  })

  it('sends printer controls as widget actions and asks before confirm-tier ones', async () => {
    const fake = server().on('GET', '/api/v1/printers/voron', 200, printerState())
    fake.handle('POST', '/api/v1/printers/voron/controls', async (request) => {
      const body = JSON.parse(request.body) as { action: string }
      return body.action === 'cancel'
        ? new Response(JSON.stringify({ status: 'confirmation_required', description: 'Cancel the print on voron', token: 'tok' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
        : new Response(JSON.stringify({ status: 'done', description: 'Paused' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
    })
    fake.on('POST', '/api/v1/printers/voron/controls/confirm', 200, { status: 'done', description: 'Cancelled' })
    const { wrapper } = await mountWidget(fake, printer)

    await wrapper.get('[data-control="pause"]').trigger('click')
    await flushPromises()
    expect(JSON.parse(fake.request('POST', '/api/v1/printers/voron/controls')!.body)).toEqual({ action: 'pause', widget: true })

    await wrapper.get('[data-control="cancel"]').trigger('click')
    await flushPromises()
    expect(document.body.textContent).toContain('Cancel the print on voron')
    expect(fake.count('POST', '/api/v1/printers/voron/controls/confirm')).toBe(0)
    ;(document.body.querySelector('[data-test="confirm"]') as HTMLButtonElement).click()
    await flushPromises()
    expect(JSON.parse(fake.request('POST', '/api/v1/printers/voron/controls/confirm')!.body)).toEqual({ token: 'tok', widget: true })
    wrapper.unmount()
  })

  it('renders the music widget, refreshes it and sends controls', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const fake = server()
      .on('GET', '/api/v1/music/kitchen', 200, { ...musicData, current: { name: 'Innuendo', artists: ['Queen'], album: null } })
      .on('POST', '/api/v1/music/kitchen/control', 200, { status: 'done' })
    const { wrapper } = await mountWidget(fake, music)

    const view = wrapper.get('[data-widget="music"]')
    expect(view.text()).toContain('Innuendo')
    expect(view.text()).toContain('Queen')
    expect(view.text()).toContain('Headlong')

    await wrapper.get('[data-control="pause"]').trigger('click')
    await flushPromises()
    expect(JSON.parse(fake.request('POST', '/api/v1/music/kitchen/control')!.body)).toEqual({ action: 'pause' })
    await wrapper.get('[data-control="next"]').trigger('click')
    await wrapper.get('input[name="volume"]').setValue('40')
    await wrapper.get('input[name="volume"]').trigger('change')
    await flushPromises()
    expect(fake.requests.filter((request) => request.method === 'POST' && request.path === '/api/v1/music/kitchen/control').map((request) => JSON.parse(request.body))).toEqual([{ action: 'pause' }, { action: 'next' }, { action: 'volume', volume: 40 }])

    const reads = fake.count('GET', '/api/v1/music/kitchen')
    vi.advanceTimersByTime(5000)
    await flushPromises()
    expect(fake.count('GET', '/api/v1/music/kitchen')).toBe(reads + 1)
    wrapper.unmount()
  })

  it('labels the music controls in German', async () => {
    const fake = server().on('GET', '/api/v1/music/kitchen', 200, musicData)
    const { wrapper } = await mountWidget(fake, music, 'de')

    expect(wrapper.get('[data-control="next"]').attributes('aria-label')).toBe('Nächster Titel')
    expect(wrapper.get('[data-widget="music"]').text()).toContain('Als Nächstes')
    wrapper.unmount()
  })

  it('renders nothing for an unknown kind', async () => {
    const { wrapper } = await mountWidget(server(), { kind: 'weather', toolCallId: 'c-3', data: {} })

    expect(wrapper.find('[data-widget]').exists()).toBe(false)
  })
})
