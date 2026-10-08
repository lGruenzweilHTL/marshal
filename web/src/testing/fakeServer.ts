/** Recorded request with its body text (read before the handler runs). */
export interface RecordedRequest {
  method: string
  path: string
  search: string
  headers: Headers
  body: string
}

type Handler = (request: RecordedRequest) => Response | Promise<Response>

/**
 * A fake Marshal API behind the `fetch` option of the API client: routes by method + path, records requests. Paths
 * may contain `{name}` segments (`/api/v1/runs/{id}`); exact paths win over patterns.
 */
export class FakeServer {
  readonly requests: RecordedRequest[] = []
  private readonly routes = new Map<string, Handler>()
  private readonly patterns: { method: string; pattern: RegExp; key: string }[] = []
  /** Answers requests no route matches (default: 404). */
  fallback: Handler = () => json(404, { title: 'not found' })

  on(method: string, path: string, status: number, body?: unknown, headers?: Record<string, string>): this {
    return this.handle(method, path, () => json(status, body, headers))
  }

  handle(method: string, path: string, handler: Handler): this {
    const key = `${method} ${path}`
    if (path.includes('{') && !this.routes.has(key)) {
      const pattern = new RegExp(`^${path.replace(/[.*+?^$()|[\]\\]/g, '\\$&').replace(/\{[^}]+\}/g, '[^/]+')}$`)
      this.patterns.push({ method, pattern, key })
    }
    this.routes.set(key, handler)
    return this
  }

  antiforgery(token: string): this {
    return this.on('GET', '/api/v1/auth/antiforgery', 200, { token })
  }

  request(method: string, path: string): RecordedRequest | undefined {
    return this.requests.findLast((request) => request.method === method && request.path === path)
  }

  count(method: string, path: string): number {
    return this.requests.filter((request) => request.method === method && request.path === path).length
  }

  /**
   * Upload transport: reports half, then routes like {@link fetch}. The body is recorded as simple multipart text
   * (`name="file"; filename="cube.stl"` and the content), since jsdom's FormData cannot go into a Node `Request`.
   */
  readonly upload = async (url: string, body: FormData, headers: Record<string, string>, onProgress: (fraction: number) => void): Promise<Response> => {
    onProgress(0.5)
    const parts: string[] = []
    for (const [name, value] of body.entries()) {
      parts.push(typeof value === 'string' ? `name="${name}"\n${value}` : `name="${name}"; filename="${value.name}"\n${await value.text()}`)
    }
    const { pathname, search } = new URL(url)
    return this.dispatch({ method: 'POST', path: pathname, search, headers: new Headers(headers), body: parts.join('\n--\n') })
  }

  readonly fetch = async (input: RequestInfo | URL, init?: RequestInit): Promise<Response> => {
    const request = input instanceof Request ? input : new Request(input, init)
    const url = new URL(request.url)
    const recorded: RecordedRequest = {
      method: request.method,
      path: url.pathname,
      search: url.search,
      headers: request.headers,
      body: request.body ? await request.text() : '',
    }
    return this.dispatch(recorded)
  }

  private dispatch(recorded: RecordedRequest): Response | Promise<Response> {
    this.requests.push(recorded)
    const key =
      this.routes.has(`${recorded.method} ${recorded.path}`) ? `${recorded.method} ${recorded.path}`
      : this.patterns.find((route) => route.method === recorded.method && route.pattern.test(recorded.path))?.key
    const handler = key ? this.routes.get(key) : undefined
    return (handler ?? this.fallback)(recorded)
  }
}

export function json(status: number, body?: unknown, headers?: Record<string, string>): Response {
  const noBody = status === 204 || body === undefined
  return new Response(noBody ? null : JSON.stringify(body), { status, headers: { ...(noBody ? {} : { 'Content-Type': 'application/json' }), ...headers } })
}

/** A text/event-stream response that delivers each chunk separately. */
export function eventStream(chunks: string[]): Response {
  const encoder = new TextEncoder()
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk))
      controller.close()
    },
  })
  return new Response(stream, { status: 200, headers: { 'Content-Type': 'text/event-stream' } })
}
