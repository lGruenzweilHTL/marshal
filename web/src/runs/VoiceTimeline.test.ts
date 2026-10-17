import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import VoiceTimeline from './VoiceTimeline.vue'
import { runStep } from '../testing/fixtures'
import { FakeServer } from '../testing/fakeServer'
import { mountOptions } from '../testing/mountApp'

const steps = [
  runStep({ sequence: 0, kind: 'context', durationMs: 300 }),
  runStep({ sequence: 1, kind: 'modelCall', durationMs: 1200, firstTokenMs: 250 }),
  runStep({ sequence: 2, kind: 'toolCall', tool: 'printer.snapshot', durationMs: 2000 }),
  runStep({ sequence: 3, kind: 'modelCall', durationMs: 500, firstTokenMs: 100 }),
  runStep({ sequence: 4, kind: 'reply' }),
]

describe('VoiceTimeline', () => {
  it('lays out context, model calls and tools in order on one scale with the first delta marked', async () => {
    const { options } = await mountOptions(new FakeServer(), '/runs/r-1')
    const wrapper = mount(VoiceTimeline, { ...options, props: { steps, totalMs: 4000, firstDeltaMs: 1600 } })

    const segments = wrapper.findAll('[data-segment]')
    expect(segments.map((segment) => segment.attributes('data-segment'))).toEqual(['context', 'modelCall', 'toolCall', 'modelCall'])
    expect(segments[0]!.attributes('style')).toContain('left: 0%')
    expect(segments[0]!.attributes('style')).toContain('width: 7.5%')
    expect(segments[2]!.attributes('style')).toContain('left: 37.5%')
    expect(segments[2]!.attributes('style')).toContain('width: 50%')
    expect(segments[2]!.text()).toContain('printer.snapshot')
    const marker = wrapper.get('[data-test="first-delta"]')
    expect(marker.attributes('style')).toContain('left: 40%')
    expect(marker.text()).toContain('1.6 s')
    expect(wrapper.text()).toContain('4 s')
  })

  it('marks the first token of each model call', async () => {
    const { options } = await mountOptions(new FakeServer(), '/runs/r-1')
    const wrapper = mount(VoiceTimeline, { ...options, props: { steps, totalMs: 4000, firstDeltaMs: null } })

    expect(wrapper.findAll('[data-test="first-token"]')).toHaveLength(2)
    expect(wrapper.find('[data-test="first-delta"]').exists()).toBe(false)
  })
})
