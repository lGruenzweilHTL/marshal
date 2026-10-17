import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { createApiSession } from '../../api/client'
import { createPreviewServer, shiftTimes } from './index'
import { anchor, conversationDetails, normal, runDetails } from './normal'
import { readEvents } from '../../chat/sse'

const web = join(__dirname, '..', '..', '..')
const openapi = JSON.parse(readFileSync(join(web, 'openapi', 'v1.json'), 'utf8')) as {
  paths: Record<string, Record<string, { responses: Record<string, { content?: Record<string, { schema: Schema }> }> }>>
  components: { schemas: Record<string, Schema> }
}

interface Schema {
  $ref?: string
  type?: string | string[]
  enum?: unknown[]
  properties?: Record<string, Schema>
  required?: string[]
  items?: Schema
  additionalProperties?: Schema | boolean
  oneOf?: Schema[]
  anyOf?: Schema[]
  allOf?: Schema[]
  format?: string
}

/** Problems of `value` against an OpenAPI schema (types, required properties, enums); enough to catch drift. */
function check(value: unknown, schema: Schema, at: string): string[] {
  if (schema.$ref) return check(value, openapi.components.schemas[schema.$ref.split('/').at(-1)!]!, at)
  for (const options of [schema.oneOf, schema.anyOf]) {
    if (options) return options.some((option) => check(value, option, at).length === 0) ? [] : [`${at}: matches no option`]
  }
  if (schema.allOf) return schema.allOf.flatMap((part) => check(value, part, at))
  const types = schema.type === undefined ? [] : Array.isArray(schema.type) ? schema.type : [schema.type]
  if (value === null) return types.length === 0 || types.includes('null') ? [] : [`${at}: null`]
  if (schema.enum && !schema.enum.includes(value)) return [`${at}: ${JSON.stringify(value)} not in ${schema.enum.join(', ')}`]
  if (types.includes('array')) return Array.isArray(value) ? value.flatMap((item, index) => check(item, schema.items ?? {}, `${at}[${index}]`)) : [`${at}: not an array`]
  if (types.includes('object') || schema.properties) {
    if (typeof value !== 'object' || Array.isArray(value)) return [`${at}: not an object`]
    const record = value as Record<string, unknown>
    const missing = (schema.required ?? []).filter((key) => !(key in record)).map((key) => `${at}.${key}: missing`)
    const props = Object.entries(record).flatMap(([key, item]) => {
      const property = schema.properties?.[key] ?? (typeof schema.additionalProperties === 'object' ? schema.additionalProperties : undefined)
      return property ? check(item, property, `${at}.${key}`) : schema.properties ? [`${at}.${key}: unknown property`] : []
    })
    return [...missing, ...props]
  }
  if (types.includes('string')) return typeof value === 'string' ? [] : [`${at}: not a string`]
  if (types.includes('integer') || types.includes('number')) return typeof value === 'number' ? [] : [`${at}: not a number`]
  if (types.includes('boolean')) return typeof value === 'boolean' ? [] : [`${at}: not a boolean`]
  return []
}

function sources(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name)
    if (statSync(path).isDirectory()) return name === 'preview' ? [] : sources(path)
    return /\.(vue|ts)$/.test(name) && !name.endsWith('.test.ts') && !name.endsWith('.d.ts') ? [readFileSync(path, 'utf8')] : []
  })
}

describe('preview scenarios', () => {
  it('has normal data for every GET endpoint the SPA calls', () => {
    const code = sources(join(web, 'src')).join('\n')
    const called = [...code.matchAll(/\.GET\(\s*'([^']+)'/g)].map((match) => match[1]!)
    const missing = [...new Set(called)].filter((path) => !(path in normal))
    expect(missing).toEqual([])
  })

  it('matches the OpenAPI response schemas', () => {
    const problems = Object.entries(normal).flatMap(([path, body]) => {
      const schema = openapi.paths[path]?.get?.responses['200']?.content?.['application/json']?.schema
      return schema ? check(body, schema, path) : [`${path}: not in openapi/v1.json`]
    })
    expect(problems).toEqual([])
  })

  it('notices data that does not match the schema', () => {
    const schema = openapi.paths['/api/v1/tasks/kill-switch']!.get!.responses['200']!.content!['application/json']!.schema
    expect(check({ active: 'yes' }, schema, 'kill-switch')).toEqual(['kill-switch.active: not a boolean'])
    expect(check({}, schema, 'kill-switch')).toEqual(['kill-switch.active: missing'])
  })

  it('matches the conversation detail schema and shows reasoning and tool steps', () => {
    const schema = openapi.paths['/api/v1/conversations/{id}']!.get!.responses['200']!.content!['application/json']!.schema
    expect(Object.entries(conversationDetails).flatMap(([id, detail]) => check(detail, schema, id))).toEqual([])
    const turns = Object.values(conversationDetails).flatMap((detail) => detail.entries.flatMap((entry) => (entry.kind === 'turn' ? [entry] : [])))
    expect(turns.some((turn) => turn.reasoning && turn.tools.length > 0)).toBe(true)
  })

  it('matches the run detail schema and has a voice run with a latency timeline', () => {
    const schema = openapi.paths['/api/v1/runs/{id}']!.get!.responses['200']!.content!['application/json']!.schema
    expect(Object.entries(runDetails).flatMap(([id, detail]) => check(detail, schema, id))).toEqual([])
    expect(Object.values(runDetails).some((detail) => detail.run.inputMode === 'voice' && detail.run.timeToFirstDeltaMs != null)).toBe(true)
  })

  it('streams reasoning and tool events before the reply', async () => {
    const response = await createPreviewServer('normal', 'en', anchor).fetch(new Request('https://marshal.test/api/v1/conversation/turns', { method: 'POST', body: '{}' }))
    const events: string[] = []
    for await (const { event } of readEvents(response.body!)) events.push(event)

    const firstDelta = events.indexOf('delta')
    expect(events.indexOf('reasoning')).toBeGreaterThanOrEqual(0)
    expect(events.indexOf('reasoning')).toBeLessThan(firstDelta)
    expect(events.filter((event) => event === 'tool').length).toBeGreaterThanOrEqual(2)
    expect(events.lastIndexOf('tool')).toBeLessThan(firstDelta)
    expect(events.at(-1)).toBe('final')
  })

  it('moves the sample timestamps to now', () => {
    const shifted = shiftTimes({ at: '2026-10-01T09:00:00.000Z', name: '2026-10-01' }, 3_600_000)
    expect(shifted).toEqual({ at: '2026-10-01T10:00:00.000Z', name: '2026-10-01' })
  })

  it('serves errors, empty lists or no response per scenario, but always the signed-in user', async () => {
    const call = (scenario: Parameters<typeof createPreviewServer>[0]) => {
      const api = createApiSession({ baseUrl: 'https://marshal.test', fetch: createPreviewServer(scenario, 'de', anchor).fetch })
      return Promise.all([api.call((c) => c.GET('/api/v1/me')), api.call((c) => c.GET('/api/v1/tasks')), api.call((c) => c.GET('/api/v1/me/preferences'))])
    }

    const [failingMe, failingTasks] = await call('failing')
    const [, offlineTasks] = await call('offline')
    const [, emptyTasks, emptyPreferences] = await call('empty')

    expect(failingMe.data?.displayName).toBe('Lukas')
    expect(failingTasks.error).toMatchObject({ kind: 'problem', status: 500, code: 'server.unexpected' })
    expect(offlineTasks.error).toEqual({ kind: 'network' })
    expect(emptyTasks.data).toEqual([])
    expect(emptyPreferences.data?.language).toBe('de')
  })
})
