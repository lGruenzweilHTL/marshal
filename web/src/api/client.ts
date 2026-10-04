import createClient, { type Client, type Middleware } from 'openapi-fetch'
import { ApiFailure, fromResponse, fromThrown, problemBody, type ApiError, type ApiResult } from './errors'
import type { components, paths } from './schema'

export type ApiClient = Client<paths>
export type Schemas = components['schemas']
export type { ApiError, ApiResult } from './errors'

/** The app's root URL without a trailing slash (the host rewrites <base href> to the configured base path). */
export function appBaseUrl(): string {
  return new URL('.', document.baseURI).href.replace(/\/$/, '')
}

/** What an `openapi-fetch` request resolves to. */
interface FetchResult<T> {
  data?: T
  error?: unknown
  response: Response
}

/**
 * The API as views see it. Every request goes through {@link Api.call}, which never throws: failures come back as an
 * {@link ApiError}. The raw `openapi-fetch` client stays inside `web/src/api/`.
 */
export interface Api {
  /**
   * Runs one typed request and returns its data or why it failed.
   * @example const result = await api.call((c) => c.GET('/api/v1/tasks'))
   */
  call<T>(request: (client: ApiClient) => Promise<FetchResult<T>>): Promise<ApiResult<T>>
  /** Root URL of the app and API (no trailing slash), for links the browser follows itself. */
  baseUrl: string
  /** Plain GET against the app root, for unversioned endpoints outside the OpenAPI document (`/health`). Any status is data. */
  fetch(path: string): Promise<ApiResult<Response>>
  /** Antiforgery token for a form the browser posts itself (fetched once, cached until reset). */
  antiforgeryToken(): Promise<ApiResult<string>>
  /** Drops the cached antiforgery token; the token is bound to the signed-in user, so call it after login and logout. */
  resetAntiforgery(): void
  /** Called with every 401 that a call returns (the user session turns it into "session expired"). */
  onUnauthorized(listener: (() => void) | null): void
}

/**
 * The API session inside `web/src/api/`: the views' {@link Api} plus the raw client.
 * @internal Only `web/src/api/` and the app entry use `raw`; views use {@link Api}.
 */
export interface ApiSession extends Api {
  raw: ApiClient
}

export function createApiSession(options: { baseUrl?: string; fetch?: typeof globalThis.fetch } = {}): ApiSession {
  const baseUrl = options.baseUrl ?? appBaseUrl()
  const doFetch = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => globalThis.fetch(input, init))
  let token: Promise<string> | null = null
  let unauthorized: (() => void) | null = null

  function antiforgery(): Promise<string> {
    token ??= doFetch(new Request(`${baseUrl}/api/v1/auth/antiforgery`, { credentials: 'same-origin' }))
      .then(async (response) => {
        if (!response.ok) throw new ApiFailure(fromResponse(response.status, await problemBody(response)))
        return ((await response.json()) as Schemas['AntiforgeryToken']).token
      })
      .catch((error: unknown) => {
        token = null
        throw error
      })
    return token
  }

  const raw = createClient<paths>({ baseUrl, fetch: doFetch, credentials: 'same-origin' })
  const tokenMiddleware: Middleware = {
    async onRequest({ request }) {
      if (request.method !== 'GET' && request.method !== 'HEAD') request.headers.set('X-XSRF-TOKEN', await antiforgery())
      return request
    },
  }
  raw.use(tokenMiddleware)

  function failed<T>(error: ApiError): ApiResult<T> {
    if (error.kind === 'problem' && error.status === 401) unauthorized?.()
    return { error }
  }

  return {
    raw,
    baseUrl,
    async call<T>(request: (client: ApiClient) => Promise<FetchResult<T>>): Promise<ApiResult<T>> {
      try {
        const { data, error, response } = await request(raw)
        if (!response.ok) return failed(fromResponse(response.status, error))
        return { data: data as T }
      } catch (cause) {
        return failed(fromThrown(cause))
      }
    },
    async fetch(path) {
      try {
        return { data: await doFetch(new Request(`${baseUrl}${path}`, { credentials: 'same-origin' })) }
      } catch (cause) {
        return { error: fromThrown(cause) }
      }
    },
    async antiforgeryToken() {
      try {
        return { data: await antiforgery() }
      } catch (cause) {
        return { error: fromThrown(cause) }
      }
    },
    resetAntiforgery() {
      token = null
    },
    onUnauthorized(listener) {
      unauthorized = listener
    },
  }
}
