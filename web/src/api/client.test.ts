import { describe, expect, it } from 'vitest'
import { createApiSession } from './client'
import { FakeServer } from '../testing/fakeServer'

describe('api client', () => {
  it('sends the antiforgery token on state-changing requests only', async () => {
    const server = new FakeServer().antiforgery('t-1').on('POST', '/api/v1/auth/logout', 204).on('GET', '/api/v1/me', 200, { id: 'u' })
    const { api } = createApiSession({ baseUrl: 'https://marshal.test', fetch: server.fetch })

    await api.GET('/api/v1/me')
    await api.POST('/api/v1/auth/logout')

    expect(server.request('GET', '/api/v1/me')?.headers.get('X-XSRF-TOKEN')).toBeNull()
    expect(server.request('POST', '/api/v1/auth/logout')?.headers.get('X-XSRF-TOKEN')).toBe('t-1')
  })

  it('fetches the token once until it is reset', async () => {
    const server = new FakeServer().antiforgery('t-1').on('POST', '/api/v1/auth/logout', 204)
    const session = createApiSession({ baseUrl: 'https://marshal.test', fetch: server.fetch })

    await session.api.POST('/api/v1/auth/logout')
    await session.api.POST('/api/v1/auth/logout')
    session.resetAntiforgery()
    await session.api.POST('/api/v1/auth/logout')

    expect(server.count('GET', '/api/v1/auth/antiforgery')).toBe(2)
  })
})
