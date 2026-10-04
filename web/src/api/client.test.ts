import { describe, expect, it } from 'vitest'
import { createApiSession } from './client'
import { FakeServer, json } from '../testing/fakeServer'

const base = 'https://marshal.test'

describe('api client', () => {
  it('sends the antiforgery token on state-changing requests only', async () => {
    const server = new FakeServer().antiforgery('t-1').on('POST', '/api/v1/auth/logout', 204).on('GET', '/api/v1/me', 200, { id: 'u' })
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })

    await api.call((c) => c.GET('/api/v1/me'))
    await api.call((c) => c.POST('/api/v1/auth/logout'))

    expect(server.request('GET', '/api/v1/me')?.headers.get('X-XSRF-TOKEN')).toBeNull()
    expect(server.request('POST', '/api/v1/auth/logout')?.headers.get('X-XSRF-TOKEN')).toBe('t-1')
  })

  it('fetches the token once until it is reset', async () => {
    const server = new FakeServer().antiforgery('t-1').on('POST', '/api/v1/auth/logout', 204)
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })

    await api.call((c) => c.POST('/api/v1/auth/logout'))
    await api.call((c) => c.POST('/api/v1/auth/logout'))
    api.resetAntiforgery()
    await api.call((c) => c.POST('/api/v1/auth/logout'))

    expect(server.count('GET', '/api/v1/auth/antiforgery')).toBe(2)
  })
})

describe('call', () => {
  it('returns the data of a successful response', async () => {
    const server = new FakeServer().on('GET', '/api/v1/me', 200, { id: 'u', displayName: 'Lukas', roles: [], scopes: [] })
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })

    const result = await api.call((c) => c.GET('/api/v1/me'))

    expect(result.error).toBeUndefined()
    expect(result.data?.displayName).toBe('Lukas')
  })

  it('returns a problem error with status, code, detail and trace id', async () => {
    const server = new FakeServer().on('GET', '/api/v1/me', 500, { code: 'server.unexpected', detail: 'Something went wrong.', traceId: '00-abc-01' })
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })

    const result = await api.call((c) => c.GET('/api/v1/me'))

    expect(result.error).toEqual({ kind: 'problem', status: 500, code: 'server.unexpected', detail: 'Something went wrong.', traceId: '00-abc-01' })
  })

  it('returns a problem error without details when the body is not JSON', async () => {
    const server = new FakeServer().handle('GET', '/api/v1/me', () => new Response('Bad gateway', { status: 502, headers: { 'Content-Type': 'text/html' } }))
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })

    const result = await api.call((c) => c.GET('/api/v1/me'))

    expect(result.error).toMatchObject({ kind: 'problem', status: 502, code: undefined, detail: undefined })
  })

  it('returns a network error when fetch rejects', async () => {
    const api = createApiSession({ baseUrl: base, fetch: () => Promise.reject(new TypeError('Failed to fetch')) })

    const result = await api.call((c) => c.GET('/api/v1/me'))

    expect(result.error).toEqual({ kind: 'network' })
  })

  it('returns the problem of a failing antiforgery token fetch instead of throwing', async () => {
    const server = new FakeServer()
      .on('GET', '/api/v1/auth/antiforgery', 400, { code: 'auth.https_required', detail: 'Browser sessions require HTTPS.' })
      .on('POST', '/api/v1/auth/local/setup', 204)
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })

    const result = await api.call((c) => c.POST('/api/v1/auth/local/setup', { body: { userName: 'lukas', displayName: 'Lukas', password: 'x' } as never }))

    expect(result.error).toMatchObject({ kind: 'problem', status: 400, code: 'auth.https_required' })
    expect(server.count('POST', '/api/v1/auth/local/setup')).toBe(0)
  })

  it('returns an unexpected error when reading the response throws', async () => {
    const server = new FakeServer().handle('GET', '/api/v1/me', () => new Response('{not json', { status: 200, headers: { 'Content-Type': 'application/json' } }))
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })

    const result = await api.call((c) => c.GET('/api/v1/me'))

    expect(result.error?.kind).toBe('unexpected')
  })

  it('reports a 401 to the unauthorized listener', async () => {
    const server = new FakeServer().on('GET', '/api/v1/tasks', 401)
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })
    let calls = 0
    api.onUnauthorized(() => calls++)

    await api.call((c) => c.GET('/api/v1/tasks'))

    expect(calls).toBe(1)
  })

  it('returns any status of a plain fetch as data and a rejected fetch as a network error', async () => {
    const server = new FakeServer().handle('GET', '/health', () => json(503, { status: 'unhealthy' }))
    const api = createApiSession({ baseUrl: base, fetch: server.fetch })
    const offline = createApiSession({ baseUrl: base, fetch: () => Promise.reject(new TypeError('offline')) })

    expect((await api.fetch('/health')).data?.status).toBe(503)
    expect((await offline.fetch('/health')).error).toEqual({ kind: 'network' })
  })
})
