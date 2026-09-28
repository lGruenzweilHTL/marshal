import type { LocationQuery } from 'vue-router'

/** The in-app path to continue with after login; anything that could leave the app falls back to `/`. */
export function returnUrl(query: LocationQuery): string {
  const value = query.returnUrl
  return typeof value === 'string' && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/\\') ? value : '/'
}
