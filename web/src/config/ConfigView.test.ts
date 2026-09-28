import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ConfigView from './ConfigView.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const admin = { ...me, roles: ['admin'], scopes: ['conversation:write', 'admin'] }

const summary = {
  errors: [
    { file: 'risk.yaml', message: 'risk[0].tier must be safe, confirm or forbidden.', previousVersionActive: true },
    { file: 'skills/broken/SKILL.md', message: 'broken/SKILL.md: frontmatter is missing.', previousVersionActive: false },
  ],
  skills: [{ name: 'weather', description: 'Answers weather questions.', files: ['references/codes.md'] }],
  templates: [
    { name: 'default', description: 'General assistant for conversation turns.', modelTier: null, tools: ['*'], skills: null, maxSteps: null, maxAdvisorCalls: null, builtIn: true },
    { name: 'rss-scorer', description: 'Scores feed items.', modelTier: 'fast', tools: ['rss.*'], skills: [], maxSteps: 4, maxAdvisorCalls: 0, builtIn: false },
  ],
  mcpServers: [
    {
      id: 'ha',
      namespace: 'ha',
      transport: 'http',
      target: 'http://ha.local:8123/api/mcp',
      untrusted: false,
      authTokenSecret: 'ha_token',
      environmentSecrets: [],
      state: 'down',
      error: 'Connection refused',
      toolCount: 0,
      file: 'ha.yaml',
    },
  ],
  riskRules: [{ tool: 'ha.*', match: { domain: ['lock'] }, args: {}, tier: 'confirm' }],
  models: [
    { key: 'claude', tier: 'advisor', provider: 'anthropic', model: 'claude-opus-4-1', endpoint: null, apiKeySecret: 'anthropic_key', vision: true, tools: true, contextLength: 200000 },
  ],
  satellites: [{ id: 'assist_satellite.kids_room', verbosity: 'short', notes: null, denyTools: ['mail.*'], denyConfirm: [] }],
}

async function mountConfig(status = 200, body: unknown = summary) {
  const fake = new FakeServer().on('GET', '/api/v1/me', 200, admin).on('GET', '/api/v1/config', status, body)
  const { options } = await mountOptions(fake, '/config')
  const wrapper = mount(ConfigView, options)
  await flushPromises()
  return wrapper
}

describe('ConfigView', () => {
  it('shows skills, templates, MCP servers, risk rules, models and satellites', async () => {
    const wrapper = await mountConfig()

    expect(wrapper.get('[data-skill="weather"]').text()).toContain('Answers weather questions.')
    const scorer = wrapper.get('[data-template="rss-scorer"]').text()
    for (const part of ['fast', 'rss.*', '4']) expect(scorer).toContain(part)
    expect(wrapper.get('[data-template="default"]').text()).toContain('built-in')
    const server = wrapper.get('[data-server="ha"]').text()
    for (const part of ['down', 'Connection refused', 'http://ha.local:8123/api/mcp', 'ha_token']) expect(server).toContain(part)
    const rule = wrapper.get('[data-rule="0"]').text()
    for (const part of ['ha.*', 'domain', 'lock', 'confirm']) expect(rule).toContain(part)
    const model = wrapper.get('[data-model="claude"]').text()
    for (const part of ['anthropic', 'claude-opus-4-1', 'anthropic_key']) expect(model).toContain(part)
    expect(wrapper.get('[data-satellite="assist_satellite.kids_room"]').text()).toContain('mail.*')
  })

  it('shows failed loads and whether the previous version stays active', async () => {
    const wrapper = await mountConfig()

    const risk = wrapper.get('[data-error="risk.yaml"]').text()
    expect(risk).toContain('tier must be safe')
    expect(risk).toContain('previous version active')
    expect(wrapper.get('[data-error="skills/broken/SKILL.md"]').text()).toContain('skipped')
  })

  it('has no editing controls', async () => {
    const wrapper = await mountConfig()

    expect(wrapper.findAll('input, textarea, select, button')).toHaveLength(0)
  })

  it('explains a missing admin scope', async () => {
    const wrapper = await mountConfig(403, { status: 403 })

    expect(wrapper.get('[role="alert"]').text()).toContain('admin')
  })
})
