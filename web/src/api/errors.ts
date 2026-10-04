/** Why an API call failed. Every failure of {@link Api.call} is one of these; `call` never throws. */
export type ApiError =
  /** No response: the server or the network could not be reached. */
  | { kind: 'network' }
  /** An error status, with the ProblemDetails `code`, `detail` and `traceId` when the body carries them. */
  | { kind: 'problem'; status: number; code?: string; detail?: string; traceId?: string }
  /** Anything thrown while preparing the request or reading the response. */
  | { kind: 'unexpected'; cause: unknown }

/** Result of an API call: the response data, or why it failed. Check `error` first; `data` may be `undefined` on 204. */
export type ApiResult<T> = { data: T; error?: undefined } | { data?: undefined; error: ApiError }

/** Thrown inside the API layer (for example by the antiforgery token fetch) and turned back into an {@link ApiError}. */
export class ApiFailure extends Error {
  constructor(readonly error: ApiError) {
    super(`API call failed: ${error.kind}`)
  }
}

/** `fetch` rejects with a TypeError when no response arrives (offline, DNS, refused, CORS). */
export function isNetworkFailure(cause: unknown): boolean {
  return cause instanceof TypeError
}

/** Maps anything thrown during a call to an {@link ApiError}. */
export function fromThrown(cause: unknown): ApiError {
  if (cause instanceof ApiFailure) return cause.error
  if (isNetworkFailure(cause)) return { kind: 'network' }
  return { kind: 'unexpected', cause }
}

/** The problem error for an error response; `body` is the parsed JSON body when there was one. */
export function fromResponse(status: number, body: unknown): ApiError {
  const problem = body && typeof body === 'object' ? (body as Record<string, unknown>) : {}
  const text = (key: string) => (typeof problem[key] === 'string' && problem[key] !== '' ? (problem[key] as string) : undefined)
  return { kind: 'problem', status, code: text('code'), detail: text('detail'), traceId: text('traceId') }
}

/** Reads an error response body as JSON when it is JSON; anything else counts as no body. */
export async function problemBody(response: Response): Promise<unknown> {
  if (!(response.headers.get('Content-Type') ?? '').includes('json')) return undefined
  try {
    return await response.json()
  } catch {
    return undefined
  }
}
