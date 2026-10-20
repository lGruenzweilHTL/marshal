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
    runStep({
      sequence: 2,
      kind: 'modelCall',
      model: 'qwen3:8b',
      tier: 'fast',
      tokensIn: 1200,
      tokensOut: 80,
      tokensCached: 1000,
      firstTokenMs: 310,
      durationMs: 900,
      cost: 0.0004,
      reasoning: 'The mail asks to remember a code.',
    }),
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
    runStep({ sequence: 6, kind: 'modelCall', model: 'qwen3:8b', tier: 'fast', reasoningWithheld: true }),
  ],
}

async function mountDetail() {
  const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/runs/r-9', 200, detail)
  const { options } = await mountOptions(fake, '/runs/r-9')
  const wrapper = mount(RunDetailView, options)
  await flushPromises()
  return wrapper
}

async function mountVoice(inputMode: 'voice' | 'text') {
  const voice = {
    run: runSummary({ id: 'r-7', inputMode, startedAt: '2026-09-28T08:00:00Z', endedAt: '2026-09-28T08:00:04Z', timeToFirstDeltaMs: 1600 }),
    language: 'de',
    taintSources: [],
    loadedNamespaces: [{ name: 'agent', reason: 'core', score: null }, { name: 'printer', reason: 'retrieval', score: 0.71 }],
    steps: [
      runStep({
        sequence: 0,
        kind: 'context',
        durationMs: 300,
        notes: [
          { kind: 'tool_namespace', label: 'agent', detail: 'core', score: null },
          { kind: 'tool_namespace', label: 'printer', detail: 'retrieval', score: 0.71 },
        ],
      }),
      runStep({ sequence: 1, kind: 'modelCall', durationMs: 1200, firstTokenMs: 250 }),
      runStep({ sequence: 2, kind: 'toolCall', tool: 'printer.snapshot', durationMs: 2000 }),
      runStep({ sequence: 3, kind: 'reply', content: 'Läuft.' }),
    ],
  }
  const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/runs/r-7', 200, voice)
  const { options } = await mountOptions(fake, '/runs/r-7')
  const wrapper = mount(RunDetailView, options)
  await flushPromises()
  return wrapper
}

async function mountRated(roles: string[], evalCaseId: string | null = null) {
  const rated = { ...detail, run: runSummary({ id: 'r-9', rating: 'down', ratingNote: 'wrong light' }), evalCaseId }
  const fake = new FakeServer()
    .antiforgery('t')
    .on('GET', '/api/v1/me', 200, { ...me, roles })
    .on('GET', '/api/v1/runs/r-9', 200, rated)
    .on('POST', '/api/v1/runs/r-9/eval-case', 200, { id: 'c-1' })
    .on('PUT', '/api/v1/runs/r-9/rating', 200, { run: 'r-9', rating: 'up', note: null, caseId: null })
  const { options } = await mountOptions(fake, '/runs/r-9')
  const wrapper = mount(RunDetailView, options)
  await flushPromises()
  return Object.assign(wrapper, { fake })
}

describe('RunDetailView', () => {
  it('shows the rating with its note and lets the owner change it', async () => {
    const wrapper = await mountRated(['user'])

    const rating = wrapper.get('[data-test="run-rating"]')
    expect(rating.get('[data-test="rate-down"]').attributes('aria-pressed')).toBe('true')
    expect(rating.get('[data-test="rating-note"]').text()).toBe('wrong light')
    expect(rating.find('[data-test="save-case"]').exists()).toBe(false)

    await rating.get('[data-test="rate-up"]').trigger('click')
    await flushPromises()

    expect(JSON.parse(wrapper.fake.request('PUT', '/api/v1/runs/r-9/rating')!.body)).toEqual({ rating: 'up', note: null })
    expect(wrapper.get('[data-test="rate-up"]').attributes('aria-pressed')).toBe('true')
  })

  it('lets an admin save the run as an eval case and then links to it', async () => {
    const wrapper = await mountRated(['admin'])

    await wrapper.get('[data-test="save-case"]').trigger('click')
    await flushPromises()

    expect(wrapper.fake.request('POST', '/api/v1/runs/r-9/eval-case')).toBeDefined()
    expect(wrapper.get('[data-test="open-case"]').attributes('href')).toBe('/system/evals/cases/c-1')
  })

  it('links a step denied by the task tool policy to its task', async () => {
    const denied = { ...detail, taskId: 't-4', steps: [runStep({ sequence: 1, kind: 'toolCall', tool: 'mail.search', error: 'tool.outside_policy: mail.search is outside this task\'s tool policy.' })] }
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/runs/r-9', 200, denied)
    const { options } = await mountOptions(fake, '/runs/r-9')
    const wrapper = mount(RunDetailView, options)
    await flushPromises()

    expect(wrapper.get('[data-test="widen-policy"]').attributes('href')).toBe('/tasks/t-4')
  })

  it('offers no policy link for other step errors', async () => {
    const failed = { ...detail, taskId: 't-4', steps: [runStep({ sequence: 1, kind: 'toolCall', tool: 'mail.search', error: 'timeout' })] }
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/runs/r-9', 200, failed)
    const { options } = await mountOptions(fake, '/runs/r-9')
    const wrapper = mount(RunDetailView, options)
    await flushPromises()

    expect(wrapper.find('[data-test="widen-policy"]').exists()).toBe(false)
  })

  it('links to an existing eval case', async () => {
    const wrapper = await mountRated(['admin'], 'c-7')

    expect(wrapper.get('[data-test="open-case"]').attributes('href')).toBe('/system/evals/cases/c-7')
    expect(wrapper.find('[data-test="save-case"]').exists()).toBe(false)
  })

  it('shows a voice run timeline with the first words marked', async () => {
    const wrapper = await mountVoice('voice')

    const timeline = wrapper.get('[data-test="voice-timeline"]')
    expect(timeline.findAll('[data-segment]')).toHaveLength(3)
    expect(timeline.get('[data-test="first-delta"]').text()).toContain('1.6 s')
    expect(wrapper.text()).toContain('First words')
  })

  it('shows no timeline for text runs', async () => {
    const wrapper = await mountVoice('text')

    expect(wrapper.find('[data-test="voice-timeline"]').exists()).toBe(false)
  })

  it('lists the loaded tool namespaces with their reasons in the context step', async () => {
    const wrapper = await mountVoice('voice')

    const notes = wrapper.get('[data-notes="tool_namespace"]')
    expect(notes.text()).toContain('Tools loaded')
    expect(notes.text()).toContain('printer – retrieval (0.71)')
  })

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

  it('shows the reasoning of a model call collapsed and expands it on request', async () => {
    const wrapper = await mountDetail()

    const reasoning = wrapper.get('[data-step="2"] details')
    expect(reasoning.attributes('open')).toBeUndefined()
    expect(reasoning.get('[data-test="reasoning-text"]').text()).toBe('The mail asks to remember a code.')
    ;(reasoning.element as HTMLDetailsElement).open = true
    await reasoning.trigger('toggle')
    expect(wrapper.get('[data-step="2"] details').attributes('open')).toBeDefined()
    expect(wrapper.find('[data-step="5"] details').exists()).toBe(false)
  })

  it('notes withheld reasoning', async () => {
    const wrapper = await mountDetail()

    expect(wrapper.get('[data-step="6"] [data-test="reasoning-withheld"]').text()).toContain('not kept')
  })

  it('reports a missing run', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/runs/nope', 404, { code: 'run.not_found' })
    const { options } = await mountOptions(fake, '/runs/nope')
    const wrapper = mount(RunDetailView, options)
    await flushPromises()

    expect(wrapper.text()).toContain('This run does not exist.')
  })
})
