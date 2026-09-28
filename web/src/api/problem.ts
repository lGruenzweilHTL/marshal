/** The message of a problem response body, or `fallback`. */
export function problemMessage(error: unknown, fallback: string): string {
  if (error && typeof error === 'object' && 'detail' in error && typeof error.detail === 'string' && error.detail) return error.detail
  return fallback
}
