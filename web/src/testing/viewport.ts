import { afterEach, vi } from 'vitest'

/** Stubs `matchMedia` so `useDesktop()` sees a desktop (≥ 900 px) or phone viewport; restored after each test. */
export function useViewport(kind: 'desktop' | 'phone'): void {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('min-width: 900px') ? kind === 'desktop' : false,
    media: query,
    addEventListener() {},
    removeEventListener() {},
  }))
}

afterEach(() => vi.unstubAllGlobals())
