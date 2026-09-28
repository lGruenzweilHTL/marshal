import createClient, { type Client, type Middleware } from 'openapi-fetch'
import type { components, paths } from './schema'

export type ApiClient = Client<paths>
export type Schemas = components['schemas']

/** The app's root URL without a trailing slash (the host rewrites <base href> to the configured base path). */
export function appBaseUrl(): string {
  return new URL('.', document.baseURI).href.replace(/\/$/, '')
}

/**
 * Typed API client. Every state-changing request carries the antiforgery token the server requires for browser
 * sessions; the token is bound to the signed-in user, so call {@link ApiSession.resetAntiforgery} after login and
 * logout.
 */
export interface ApiSession {
  api: ApiClient
  /** Root URL of the app and API (no trailing slash), for links the browser follows itself. */
  baseUrl: string
  /** Antiforgery token for a state-changing request (fetched once, cached until reset). */
  antiforgeryToken(): Promise<string>
  resetAntiforgery(): void
}

export function createApiSession(options: { baseUrl?: string; fetch?: typeof globalThis.fetch } = {}): ApiSession {
  const baseUrl = options.baseUrl ?? appBaseUrl()
  const doFetch = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => globalThis.fetch(input, init))
  let token: Promise<string> | null = null

  const session: ApiSession = {
    baseUrl,
    api: createClient<paths>({ baseUrl, fetch: doFetch, credentials: 'same-origin' }),
    antiforgeryToken() {
      token ??= doFetch(new Request(`${baseUrl}/api/v1/auth/antiforgery`, { credentials: 'same-origin' }))
        .then(async (response) => {
          if (!response.ok) throw new Error(`antiforgery token: HTTP ${response.status}`)
          return ((await response.json()) as Schemas['AntiforgeryToken']).token
        })
        .catch((error: unknown) => {
          token = null
          throw error
        })
      return token
    },
    resetAntiforgery() {
      token = null
    },
  }

  const antiforgery: Middleware = {
    async onRequest({ request }) {
      if (request.method !== 'GET' && request.method !== 'HEAD') request.headers.set('X-XSRF-TOKEN', await session.antiforgeryToken())
      return request
    },
  }
  session.api.use(antiforgery)
  return session
}
