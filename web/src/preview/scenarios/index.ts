import type { Language } from '../../i18n/catalog'
import { eventStream, FakeServer, json } from '../../testing/fakeServer'
import { anchor, conversationDetails, normal, runDetails } from './normal'

export const scenarios = ['normal', 'demo', 'empty', 'failing', 'offline'] as const
export type ScenarioName = (typeof scenarios)[number]

/** Paths every scenario answers normally, so the app gets past sign-in and shows its views. */
const session = new Set(['/api/v1/me', '/api/v1/me/preferences', '/api/v1/auth/antiforgery', '/api/v1/auth/providers', '/api/v1/auth/local/setup'])

const iso = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})$/

/** Moves every timestamp in `value` by `offset` milliseconds, so the sample data happens "now". */
export function shiftTimes<T>(value: T, offset: number): T {
  if (typeof value === 'string') return (iso.test(value) ? new Date(Date.parse(value) + offset).toISOString() : value) as T
  if (Array.isArray(value)) return value.map((item) => shiftTimes(item, offset)) as T
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) => [key, shiftTimes(item, offset)])) as T
  return value
}

/** Demo mode: the normal data, with the badge on and every demo integration simulated. */
function demo(path: string, body: unknown): unknown {
  if (path === '/api/v1/me') return { ...(body as object), demo: { enabled: true, showBadge: true } }
  if (path === '/api/v1/system') return { ...(body as object), simulatedIntegrations: ['home_assistant', 'printers', 'mail', 'rss', 'notes'] }
  return body
}

/** Lists become empty; objects stay (an empty scenario still has a signed-in user and a printer state). */
function emptied(path: string, body: unknown): unknown {
  if (session.has(path)) return body
  if (Array.isArray(body)) return []
  if (path === '/api/v1/me/secrets') return { secrets: [], unresolved: [] }
  if (path === '/api/v1/memory/graph') return { nodes: [], edges: [] }
  if (path === '/api/v1/system/health') return { warnings: [] }
  if (path === '/api/v1/memory/audit') return { stale: [], contradictions: [], orphans: [], danglingEdges: [] }
  if (path === '/api/v1/artifacts') return { ...(body as object), items: [], usedBytes: 0 }
  if (path === '/api/v1/cost') return { ...(body as object), days: [], total: 0, byTier: {}, advisor: 0 }
  return body
}

/** Content of the sample Markdown artifact (every artifact id serves it). */
const artifactText = '# Voron notes\n\n- Run `QUAD_GANTRY_LEVEL`, then `BED_MESH_CALIBRATE`.\n- PETG: 240 °C hotend, 80 °C bed.\n'

let trace = 0
const problem = () => json(500, { title: 'server.unexpected', code: 'server.unexpected', detail: 'Something went wrong on the server.', traceId: `00-preview${String(++trace).padStart(4, '0')}-01` })

/** A canned streamed chat reply in the turn event format: reasoning, one tool call, then the reply text. */
function chatReply(language: Language): Response {
  const de = language === 'de'
  const text = de ? 'Der Voron druckt gerade Schicht 112 von 268, noch etwa 1 h 12 min.' : 'The Voron is on layer 112 of 268, about 1 h 12 min left.'
  const thoughts = de
    ? ['Gefragt ist der Stand des Drucks. ', 'Ich frage den Status des Voron ab, ', 'statt aus dem Gedächtnis zu antworten.']
    : ['The user wants to know how the print is going. ', 'I will check the Voron status ', 'rather than answer from memory.']
  const after = de ? 'Schicht und Restzeit reichen als Antwort.' : 'Layer and time left are enough for the answer.'
  const tool = { id: 'call-1', tool: 'printer.status', arguments: 'printer=voron' }
  const event = (name: string, data: unknown) => `event: ${name}
data: ${JSON.stringify(data)}

`
  return eventStream([
    ...thoughts.map((chunk) => event('reasoning', { text: chunk })),
    event('tool', { ...tool, state: 'running' }),
    event('tool', { ...tool, state: 'ok', durationMs: 184, result: 'Printing planter_bracket.gcode, layer 112 of 268' }),
    event('reasoning', { text: after }),
    ...text.split(' ').map((word, index) => event('delta', { text: (index ? ' ' : '') + word })),
    event('final', { speech: text, continueConversation: false, conversationId: 'preview-conversation' }),
  ])
}

/**
 * The fake Marshal API of preview mode. `normal` serves the sample data, `demo` the same in demo mode, `empty` empty lists, `failing` a server error
 * for every request, `offline` no response at all; sign-in and preferences always work so the views are reachable.
 */
export function createPreviewServer(scenario: ScenarioName, language: Language, now = Date.now()): FakeServer {
  const server = new FakeServer()
  const offset = now - anchor
  for (const [path, body] of Object.entries(normal)) {
    server.handle('GET', path, () => {
      if (session.has(path)) return json(200, path === '/api/v1/me/preferences' ? { ...(body as object), language } : scenario === 'demo' ? demo(path, body) : body)
      if (scenario === 'failing') return problem()
      if (scenario === 'offline') throw new TypeError('Failed to fetch')
      return json(200, shiftTimes(scenario === 'empty' ? emptied(path, body) : scenario === 'demo' ? demo(path, body) : body, offset))
    })
  }
  server.handle('GET', '/health', () => {
    if (scenario === 'offline') throw new TypeError('Failed to fetch')
    if (scenario === 'failing') return json(503, { status: 'unhealthy', checks: { database: { status: 'unhealthy', description: 'Database is locked.', warnings: null } } })
    return json(200, {
      status: 'degraded',
      checks: {
        database: { status: 'healthy', description: null, warnings: null },
        homeassistant: { status: 'healthy', description: null, warnings: null },
        mcp: { status: 'degraded', description: 'notes-search is down.', warnings: ['notes-search: process exited with code 1'] },
      },
    })
  })
  server.handle('GET', '/api/v1/artifacts/{id}/content', () => {
    if (scenario === 'failing') return problem()
    if (scenario === 'offline') throw new TypeError('Failed to fetch')
    return new Response(artifactText, { status: 200, headers: { 'Content-Type': 'text/markdown; charset=utf-8' } })
  })
  server.handle('GET', '/api/v1/conversations/{id}', (request) => {
    if (scenario === 'failing') return problem()
    if (scenario === 'offline') throw new TypeError('Failed to fetch')
    const detail = conversationDetails[request.path.split('/').at(-1)!]
    if (!detail || scenario === 'empty') return json(404, { title: 'not found', code: 'conversation.not_found', detail: 'The conversation does not exist.' })
    return json(200, shiftTimes(detail, offset))
  })
  server.handle('GET', '/api/v1/runs/{id}', (request) => {
    if (scenario === 'failing') return problem()
    if (scenario === 'offline') throw new TypeError('Failed to fetch')
    if (scenario === 'empty') return json(404, { title: 'not found', code: 'run.not_found', detail: 'The run does not exist.' })
    const detail = runDetails[request.path.split('/').at(-1)!] ?? Object.values(runDetails)[0]!
    return json(200, shiftTimes(detail, offset))
  })
  server.handle('POST', '/api/v1/conversation/turns', () => {
    if (scenario === 'failing') return problem()
    if (scenario === 'offline') throw new TypeError('Failed to fetch')
    return chatReply(language)
  })
  server.fallback = (request) => {
    if (scenario === 'offline' && !session.has(request.path)) throw new TypeError('Failed to fetch')
    if (scenario === 'failing' && !session.has(request.path)) return problem()
    if (request.method === 'GET') return json(404, { title: 'not found', code: 'preview.no_data', detail: `No preview data for ${request.path}.` })
    return json(204)
  }
  return server
}
