import type { Language } from '../../i18n/catalog'
import { eventStream, FakeServer, json } from '../../testing/fakeServer'
import { anchor, normal } from './normal'

export const scenarios = ['normal', 'empty', 'failing', 'offline'] as const
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

/** Lists become empty; objects stay (an empty scenario still has a signed-in user and a printer state). */
function emptied(path: string, body: unknown): unknown {
  if (session.has(path)) return body
  if (Array.isArray(body)) return []
  if (path === '/api/v1/me/secrets') return { secrets: [], unresolved: [] }
  if (path === '/api/v1/memory/graph') return { nodes: [], edges: [] }
  if (path === '/api/v1/memory/audit') return { stale: [], contradictions: [], orphans: [], danglingEdges: [] }
  if (path === '/api/v1/cost') return { ...(body as object), days: [], total: 0, byTier: {}, advisor: 0 }
  return body
}

let trace = 0
const problem = () => json(500, { title: 'server.unexpected', code: 'server.unexpected', detail: 'Something went wrong on the server.', traceId: `00-preview${String(++trace).padStart(4, '0')}-01` })

/** A canned streamed chat reply in the turn event format. */
function chatReply(language: Language): Response {
  const text = language === 'de' ? 'Der Voron druckt gerade Schicht 112 von 268, noch etwa 1 h 12 min.' : 'The Voron is on layer 112 of 268, about 1 h 12 min left.'
  const words = text.split(' ')
  return eventStream([
    `event: status\ndata: ${JSON.stringify({ text: language === 'de' ? 'Frage den Drucker ab …' : 'Checking the printer…' })}\n\n`,
    ...words.map((word, index) => `event: delta\ndata: ${JSON.stringify({ text: (index ? ' ' : '') + word })}\n\n`),
    `event: final\ndata: ${JSON.stringify({ speech: text, continueConversation: false, conversationId: 'preview-conversation' })}\n\n`,
  ])
}

/**
 * The fake Marshal API of preview mode. `normal` serves the sample data, `empty` empty lists, `failing` a server error
 * for every request, `offline` no response at all; sign-in and preferences always work so the views are reachable.
 */
export function createPreviewServer(scenario: ScenarioName, language: Language, now = Date.now()): FakeServer {
  const server = new FakeServer()
  const offset = now - anchor
  for (const [path, body] of Object.entries(normal)) {
    server.handle('GET', path, () => {
      if (session.has(path)) return json(200, path === '/api/v1/me/preferences' ? { ...(body as object), language } : body)
      if (scenario === 'failing') return problem()
      if (scenario === 'offline') throw new TypeError('Failed to fetch')
      return json(200, shiftTimes(scenario === 'empty' ? emptied(path, body) : body, offset))
    })
  }
  server.handle('GET', '/health', () => {
    if (scenario === 'offline') throw new TypeError('Failed to fetch')
    if (scenario === 'failing') return json(503, { status: 'unhealthy', checks: { database: { status: 'unhealthy', description: 'Database is locked.', warnings: null } } })
    return json(200, {
      status: 'degraded',
      checks: {
        database: { status: 'healthy', description: null, warnings: null },
        homeAssistant: { status: 'healthy', description: null, warnings: null },
        mcp: { status: 'degraded', description: 'notes-search is down.', warnings: ['notes-search: process exited with code 1'] },
      },
    })
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
