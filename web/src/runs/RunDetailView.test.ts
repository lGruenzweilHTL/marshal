import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import RunDetailView from './RunDetailView.vue'
import { FakeServer } from '../testing/fakeServer'
import { runStep, runSummary } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

const detail = {
  run: runSummary({ id: 'r-9', tainted: true }),
  language: 'en',
  taintSources: [{ kind: 'mail', firstStep: 2 }],
  steps: [
    runStep({
      sequence: 1,
      kind: 'context',
      content: 'system prompt v3, 2 memory chunks',
      notes: [
        { kind: 'project', label: 'Voron', detail: 'centroid', score: 0.812 },
        { kind: 'memory', label: 'Voron nozzle', detail: 'device via vector', score: 0.64 },
        { kind: 'memory', label: 'Allergy', detail: 'fact, pinned', score: null },
      ],
    }),
    runStep({ sequence: 2, kind: 'modelCall', model: 'qwen3:8b', tier: 'fast', tokensIn: 1200, tokensOut: 80, tokensCached: 1000, firstTokenMs: 310, durationMs: 900, cost: 0.0004 }),
    runStep({
      sequence: 3,
      kind: 'toolCall',
      tool: 'memory.write',
      arguments: '{"text":"remember"}',
      riskTier: 'confirm',
      guardDecision: 'escalatedByTaint',
      reasons: [{ code: 'taint', sentence: 'The run read untrusted mail.' }],
      reference: 'blob:abc123',
      content: null,
    }),
    runStep({ sequence: 4, kind: 'reply', content: 'Should I remember that?' }),
    runStep({ sequence: 5, kind: 'modelCall', model: 'qwen3:8b', tier: 'fast', depth: 1 }),
  ],
}

async function mountDetail() {
  const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/runs/r-9', 200, detail)
  const { options } = await mountOptions(fake, '/runs/r-9')
  const wrapper = mount(RunDetailView, options)
  await flushPromises()
  return wrapper
}

describe('RunDetailView', () => {
  it('shows the guard decision escalated by taint with the taint source', async () => {
    const wrapper = await mountDetail()

    const tool = wrapper.get('[data-step="3"]')
    expect(tool.text()).toContain('memory.write')
    expect(tool.text()).toContain('escalated to confirm by taint')
    expect(tool.text()).toContain('The run read untrusted mail.')
    expect(wrapper.get('[data-test="taint"]').text()).toContain('mail')
    expect(wrapper.get('[data-test="taint"]').text()).toContain('from step 2')
  })

  it('gives every step an anchor, so the review queue can link to the step that tainted a run', async () => {
    const wrapper = await mountDetail()

    expect(wrapper.get('[data-step="2"]').attributes('id')).toBe('step-2')
  })

  it('shows the reference of a redacted result, not content', async () => {
    const wrapper = await mountDetail()

    const tool = wrapper.get('[data-step="3"]')
    expect(tool.text()).toContain('blob:abc123')
    expect(tool.find('[data-test="content"]').exists()).toBe(false)
  })

  it('shows context, model call metrics and the reply', async () => {
    const wrapper = await mountDetail()

    expect(wrapper.get('[data-step="1"]').text()).toContain('system prompt v3')
    const model = wrapper.get('[data-step="2"]').text()
    for (const part of ['qwen3:8b', 'fast', '1,200', '80', '1,000', '310 ms', '900 ms', '0.0004']) expect(model).toContain(part)
    expect(wrapper.get('[data-step="4"]').text()).toContain('Should I remember that?')
  })

  it('shows active projects and retrieved memory of the context build', async () => {
    const wrapper = await mountDetail()

    const projects = wrapper.get('[data-step="1"] [data-notes="project"]')
    expect(projects.text()).toContain('Voron')
    expect(projects.text()).toContain('centroid')
    expect(projects.text()).toContain('0.81')
    const memory = wrapper.get('[data-step="1"] [data-notes="memory"]')
    expect(memory.findAll('li').map((item) => item.text())).toEqual(['Voron nozzle – device via vector (0.64)', 'Allergy – fact, pinned'])
  })

  it('marks subagent steps with their depth', async () => {
    const wrapper = await mountDetail()

    expect(wrapper.get('[data-step="5"]').text()).toContain('subagent (depth 1)')
    expect(wrapper.get('[data-step="5"]').classes()).toContain('subagent')
    expect(wrapper.get('[data-step="2"]').text()).not.toContain('subagent')
  })

  it('reports a missing run', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/runs/nope', 404, { code: 'run.not_found' })
    const { options } = await mountOptions(fake, '/runs/nope')
    const wrapper = mount(RunDetailView, options)
    await flushPromises()

    expect(wrapper.text()).toContain('This run does not exist.')
  })
})
