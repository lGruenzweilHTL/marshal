import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import EvalCaseView from './EvalCaseView.vue'
import EvalRunView from './EvalRunView.vue'
import EvalsView from './EvalsView.vue'
import type { Schemas } from '../api/client'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const admin = { ...me, roles: ['admin'] }

function evalCase(overrides: Partial<Schemas['EvalCase']> = {}): Schemas['EvalCase'] {
  return {
    id: 'c-1',
    sourceRunId: 'r-1',
    expectation: 'bad',
    note: 'should turn off the ceiling light',
    input: 'turn off the kitchen light',
    inputMode: 'voice',
    language: 'en',
    satelliteId: null,
    areaId: 'kitchen',
    history: [],
    turnContext: 'Area: kitchen',
    toolCalls: [{ tool: 'ha.HassTurnOff', arguments: '{"name":"lamp"}', result: 'ok', error: null, untrusted: false }],
    reply: 'The lamp is off.',
    incomplete: false,
    recordedAt: '2026-10-12T08:00:00Z',
    createdAt: '2026-10-12T08:05:00Z',
    ...overrides,
  }
}

const cases = [evalCase(), evalCase({ id: 'c-2', expectation: 'good', note: null, input: 'what is the printer doing?', incomplete: true, toolCalls: [] })]

const config = {
  errors: [],
  skills: [],
  templates: [
    { name: 'default', description: '', modelTier: null, tools: [], skills: null, maxSteps: null, maxAdvisorCalls: null, builtIn: true },
    { name: 'concise', description: 'Short answers', modelTier: null, tools: [], skills: null, maxSteps: null, maxAdvisorCalls: null, builtIn: false },
  ],
  mcpServers: [],
  riskRules: [],
  models: [
    { key: 'fast', tier: 'fast', provider: 'ollama', model: 'qwen3:8b', endpoint: null, apiKeySecret: null, vision: false, tools: true, contextLength: null },
    { key: 'claude', tier: 'standard', provider: 'anthropic', model: 'claude-sonnet', endpoint: null, apiKeySecret: 'anthropic', vision: true, tools: true, contextLength: null },
  ],
  satellites: [],
}

function runView(overrides: Partial<Schemas['EvalRunView']> = {}): Schemas['EvalRunView'] {
  return {
    id: 'e-1',
    candidate: { models: { standard: 'claude' }, template: null, basePrompt: null, judgeModel: null },
    status: 'completed',
    caseIds: ['c-1', 'c-2'],
    startedBy: 'user-1',
    estimatedCost: 0.02,
    cost: 0.018,
    pass: 1,
    changed: 0,
    diverged: 1,
    failed: 0,
    createdAt: '2026-10-13T08:00:00Z',
    endedAt: '2026-10-13T08:01:00Z',
    error: null,
    ...overrides,
  }
}

async function mountAt(component: object, path: string, fake: FakeServer) {
  const { options, router } = await mountOptions(fake.antiforgery('t').on('GET', '/api/v1/me', 200, admin), path)
  const wrapper = mount(component, options)
  await flushPromises()
  return Object.assign(wrapper, { router })
}

describe('EvalsView', () => {
  function server() {
    return new FakeServer().on('GET', '/api/v1/evals/cases', 200, cases).on('GET', '/api/v1/evals/runs', 200, [runView()]).on('GET', '/api/v1/config', 200, config)
  }

  it('lists cases with expectation, note and the incomplete marker', async () => {
    const wrapper = await mountAt(EvalsView, '/system/evals', server())

    const first = wrapper.get('[data-case="c-1"]')
    expect(first.text()).toContain('turn off the kitchen light')
    expect(first.text()).toContain('bad')
    expect(first.text()).toContain('should turn off the ceiling light')
    expect(first.find('[data-test="incomplete"]').exists()).toBe(false)
    expect(wrapper.get('[data-case="c-2"] [data-test="incomplete"]').text()).toBe('incomplete')
  })

  it('estimates and starts an eval run with models per tier, template, base prompt and judge', async () => {
    const fake = server()
      .on('POST', '/api/v1/evals/estimate', 200, { estimatedCost: 0.0123 })
      .on('POST', '/api/v1/evals/runs', 201, runView({ id: 'e-9', status: 'queued' }))
    const wrapper = await mountAt(EvalsView, '/system/evals', fake)

    expect(wrapper.get('[data-test="start"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-test="select-all"] input').setValue(true)
    await wrapper.get('select[name="model-standard"]').setValue('claude')
    await wrapper.get('select[name="template"]').setValue('concise')
    await wrapper.get('textarea[name="basePrompt"]').setValue('Be brief.')
    await wrapper.get('select[name="judge"]').setValue('claude')
    await wrapper.get('[data-test="estimate-button"]').trigger('click')
    await flushPromises()

    const expected = { caseIds: ['c-1', 'c-2'], models: { standard: 'claude' }, template: 'concise', basePrompt: 'Be brief.', judgeModel: 'claude' }
    expect(JSON.parse(fake.request('POST', '/api/v1/evals/estimate')!.body)).toEqual(expected)
    expect(wrapper.get('[data-test="estimate"]').text()).toContain('0.0123')

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/evals/runs')!.body)).toEqual(expected)
    expect(wrapper.router.currentRoute.value.path).toBe('/system/evals/runs/e-9')
  })

  it('lists eval runs with their totals', async () => {
    const wrapper = await mountAt(EvalsView, '/system/evals', server())

    const row = wrapper.get('[data-run="e-1"]')
    expect(row.text()).toContain('1 pass · 0 changed · 1 diverged · 0 failed')
    expect(row.attributes('href')).toBe('/system/evals/runs/e-1')
  })

  it('says evals are for admins when the API refuses', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/evals/cases', 403, { code: 'forbidden' }).on('GET', '/api/v1/evals/runs', 403, { code: 'forbidden' }).on('GET', '/api/v1/config', 403, { code: 'forbidden' })
    const wrapper = await mountAt(EvalsView, '/system/evals', fake)

    expect(wrapper.text()).toContain('Evals are for admins.')
  })
})

describe('EvalCaseView', () => {
  it('shows the recorded turn and edits the note', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/evals/cases/c-1', 200, evalCase()).on('PUT', '/api/v1/evals/cases/c-1/note', 204, null)
    const wrapper = await mountAt(EvalCaseView, '/system/evals/cases/c-1', fake)

    expect(wrapper.get('[data-test="input"]').text()).toBe('turn off the kitchen light')
    expect(wrapper.get('[data-test="tool-call"]').text()).toContain('ha.HassTurnOff')
    expect(wrapper.text()).toContain('Area: kitchen')
    await wrapper.get('textarea[name="note"]').setValue('ceiling light, not the lamp')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('PUT', '/api/v1/evals/cases/c-1/note')!.body)).toEqual({ note: 'ceiling light, not the lamp' })
    expect(wrapper.find('[data-test="note-saved"]').exists()).toBe(true)
  })

  it('deletes the case and returns to the list', async () => {
    const fake = new FakeServer()
      .on('GET', '/api/v1/evals/cases/c-1', 200, evalCase())
      .on('DELETE', '/api/v1/evals/cases/c-1', 204, null)
      .on('GET', '/api/v1/evals/cases', 200, [])
      .on('GET', '/api/v1/evals/runs', 200, [])
      .on('GET', '/api/v1/config', 200, config)
    const wrapper = await mountAt(EvalCaseView, '/system/evals/cases/c-1', fake)

    await wrapper.get('[data-test="delete"]').trigger('click')
    await flushPromises()

    expect(wrapper.router.currentRoute.value.path).toBe('/system/evals')
  })
})

describe('EvalRunView', () => {
  it('shows totals, cost and the recorded and replayed calls side by side', async () => {
    const report = {
      run: runView(),
      results: [
        { caseId: 'c-1', outcome: 'diverged', calls: [{ tool: 'ha.HassTurnOff', arguments: '{"name":"ceiling"}', recorded: false }], reply: 'The ceiling light is off.', judgeScore: 0.9, cost: 0.01, error: null },
        { caseId: 'c-2', outcome: 'pass', calls: [], reply: 'It is printing.', judgeScore: null, cost: 0.008, error: null },
      ],
    }
    const fake = new FakeServer().on('GET', '/api/v1/evals/runs/e-1', 200, report).on('GET', '/api/v1/evals/cases', 200, cases)
    const wrapper = await mountAt(EvalRunView, '/system/evals/runs/e-1', fake)

    expect(wrapper.get('[data-total="pass"]').text()).toContain('1')
    expect(wrapper.get('[data-total="diverged"]').text()).toContain('1')
    expect(wrapper.get('[data-total="cost"]').text()).toContain('0.018')
    const result = wrapper.get('[data-result="c-1"]')
    expect(result.get('[data-side="recorded"]').text()).toContain('{"name":"lamp"}')
    expect(result.get('[data-side="recorded"]').text()).toContain('The lamp is off.')
    expect(result.get('[data-side="replayed"]').text()).toContain('{"name":"ceiling"}')
    expect(result.get('[data-side="replayed"]').text()).toContain('no recording')
    expect(result.get('[data-test="note"]').text()).toBe('should turn off the ceiling light')
    expect(result.text()).toContain('Judge score 0.90')
  })

  it('cancels a running eval run', async () => {
    const fake = new FakeServer()
      .on('GET', '/api/v1/evals/runs/e-1', 200, { run: runView({ status: 'running' }), results: [] })
      .on('GET', '/api/v1/evals/cases', 200, cases)
      .on('POST', '/api/v1/evals/runs/e-1/cancel', 204, null)
    const wrapper = await mountAt(EvalRunView, '/system/evals/runs/e-1', fake)

    await wrapper.get('[data-test="cancel"]').trigger('click')
    await flushPromises()

    expect(fake.request('POST', '/api/v1/evals/runs/e-1/cancel')).toBeDefined()
    wrapper.unmount()
  })
})
