// Compile-time checks for Catalog<T>; `vue-tsc` (npm run build) fails if a check no longer holds.
import type { Catalog } from './catalog'

const en = { nav: { home: 'Home', chat: 'Chat' }, title: 'Marshal' }

export const complete: Catalog<typeof en> = { nav: { home: 'Start', chat: 'Chat' }, title: 'Marshal' }

// @ts-expect-error A German catalog that misses an English key does not type-check.
export const missing: Catalog<typeof en> = { nav: { home: 'Start' }, title: 'Marshal' }

// @ts-expect-error A key that the English source does not have does not type-check.
export const extra: Catalog<typeof en> = { nav: { home: 'Start', chat: 'Chat', more: 'Mehr' }, title: 'Marshal' }
