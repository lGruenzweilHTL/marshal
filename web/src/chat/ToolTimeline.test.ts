import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ToolTimeline from './ToolTimeline.vue'
import { FakeServer } from '../testing/fakeServer'
import { mountOptions } from '../testing/mountApp'
import { applyTool, type ToolStep } from './turnEvents'

async function mountTimeline(steps: ToolStep[], language: 'en' | 'de' = 'en') {
  const { options } = await mountOptions(new FakeServer(), '/chat', { language })
  return mount(ToolTimeline, { ...options, props: { steps } })
}

const running = { id: 'call-1', tool: 'printer.status', arguments: 'printer=voron', state: 'running' } as const

describe('ToolTimeline', () => {
  it('keeps one line per call id and updates it when the call ends', async () => {
    const steps: ToolStep[] = []
    applyTool(steps, running)
    const wrapper = await mountTimeline(steps)
    expect(wrapper.get('[data-test="tool-step"]').attributes('data-state')).toBe('running')

    applyTool(steps, { ...running, state: 'error', durationMs: 1_240, result: 'printer.offline' })
    await wrapper.setProps({ steps: [...steps] })

    const lines = wrapper.findAll('[data-test="tool-step"]')
    expect(lines).toHaveLength(1)
    expect(lines[0]!.attributes('data-state')).toBe('error')
    expect(lines[0]!.text()).toContain('Failed')
    expect(lines[0]!.text()).toContain('1.2 s')
    expect(lines[0]!.text()).toContain('printer.offline')
  })

  it('shows label, arguments, state, duration and result of each call', async () => {
    const wrapper = await mountTimeline([
      { kind: 'tool', ...running, state: 'ok', durationMs: 184, result: 'Printing, layer 112 of 268' },
      { kind: 'tool', id: 'call-2', tool: 'door.unlock', arguments: 'door=front', state: 'denied', durationMs: 0, result: 'tool.not_available' },
      { kind: 'tool', id: 'call-3', tool: 'printer.start_print', arguments: 'file=bracket.gcode', state: 'pending', durationMs: 0, result: null },
    ])

    const [ok, denied, pending] = wrapper.findAll('[data-test="tool-step"]')
    expect(ok!.get('[data-test="tool-label"]').text()).toBe('Printer status')
    expect(ok!.text()).toContain('printer=voron')
    expect(ok!.text()).toContain('184 ms')
    expect(ok!.text()).toContain('Printing, layer 112 of 268')
    expect(denied!.get('[data-test="tool-label"]').text()).toBe('door.unlock')
    expect(denied!.text()).toContain('Not allowed')
    expect(pending!.text()).toContain('Waiting for confirmation')
  })

  it('uses the German labels', async () => {
    const wrapper = await mountTimeline([{ kind: 'tool', ...running }], 'de')

    expect(wrapper.get('[data-test="tool-label"]').text()).toBe('Druckerstatus')
    expect(wrapper.text()).toContain('Läuft')
  })

  it('shows status messages in the same list', async () => {
    const wrapper = await mountTimeline([{ kind: 'status', text: 'This takes a moment…' }, { kind: 'tool', ...running }])

    const items = wrapper.findAll('li')
    expect(items).toHaveLength(2)
    expect(items[0]!.attributes('data-test')).toBe('status-step')
    expect(items[0]!.text()).toBe('This takes a moment…')
  })

  it('renders summaries as plain text', async () => {
    const wrapper = await mountTimeline([{ kind: 'tool', ...running, state: 'ok', durationMs: 5, result: '<img src="https://evil.example/a.png"> **x**' }])

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.text()).toContain('<img src="https://evil.example/a.png"> **x**')
  })
})
