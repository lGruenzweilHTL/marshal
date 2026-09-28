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
 * A fake Marshal API behind the `fetch` option of the API client: routes by method + path, records requests.
 */
export class FakeServer {
  readonly requests: RecordedRequest[] = []
  private readonly routes = new Map<string, Handler>()

  on(method: string, path: string, status: number, body?: unknown, headers?: Record<string, string>): this {
    return this.handle(method, path, () => json(status, body, headers))
  }

  handle(method: string, path: string, handler: Handler): this {
    this.routes.set(`${method} ${path}`, handler)
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
    this.requests.push(recorded)
    const handler = this.routes.get(`${recorded.method} ${recorded.path}`)
    return handler ? handler(recorded) : json(404, { title: 'not found' })
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
