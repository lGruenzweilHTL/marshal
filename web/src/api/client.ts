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
  /**
   * Posts a multipart form (file uploads) with the antiforgery token and reports upload progress (0 to 1).
   * @example await api.upload<Schemas['ArtifactResponse']>('/api/v1/artifacts', form, (fraction) => (progress.value = fraction))
   */
  upload<T>(path: string, body: FormData, onProgress?: (fraction: number) => void): Promise<ApiResult<T>>
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

/** Sends an upload and reports progress; `fetch` has no upload progress, so the browser transport uses XMLHttpRequest. */
export type UploadTransport = (url: string, body: FormData, headers: Record<string, string>, onProgress: (fraction: number) => void) => Promise<Response>

export const xhrUpload: UploadTransport = (url, body, headers, onProgress) =>
  new Promise((resolve, reject) => {
    const request = new XMLHttpRequest()
    request.open('POST', url)
    request.withCredentials = true
    for (const [name, value] of Object.entries(headers)) request.setRequestHeader(name, value)
    request.upload.addEventListener('progress', (event) => {
      if (event.lengthComputable && event.total > 0) onProgress(event.loaded / event.total)
    })
    request.addEventListener('load', () =>
      resolve(new Response(request.status === 204 ? null : request.responseText, { status: request.status, headers: { 'Content-Type': request.getResponseHeader('Content-Type') ?? 'application/json' } })),
    )
    request.addEventListener('error', () => reject(new TypeError('Failed to fetch')))
    request.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
    request.send(body)
  })

export function createApiSession(options: { baseUrl?: string; fetch?: typeof globalThis.fetch; upload?: UploadTransport } = {}): ApiSession {
  const baseUrl = options.baseUrl ?? appBaseUrl()
  const doFetch = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => globalThis.fetch(input, init))
  const doUpload = options.upload ?? xhrUpload
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
    async upload<T>(path: string, body: FormData, onProgress?: (fraction: number) => void): Promise<ApiResult<T>> {
      try {
        const response = await doUpload(`${baseUrl}${path}`, body, { 'X-XSRF-TOKEN': await antiforgery() }, (fraction) => onProgress?.(fraction))
        if (!response.ok) return failed(fromResponse(response.status, await problemBody(response)))
        onProgress?.(1)
        return { data: (await response.json()) as T }
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
