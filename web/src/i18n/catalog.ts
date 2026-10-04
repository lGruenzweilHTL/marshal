/**
 * The shape of a translated catalog: the same keys as the English source, every leaf a string. A German catalog typed
 * as `Catalog<typeof en>` fails type checking when it misses or adds a key.
 */
export type Catalog<T> = { readonly [K in keyof T]: T[K] extends string ? string : Catalog<T[K]> }

/** Interface languages; the first is the fallback. */
export const languages = ['en', 'de'] as const
export type Language = (typeof languages)[number]

/** The supported language for a BCP 47 tag (`de-AT` → `de`), or null. */
export function supportedLanguage(tag: string | null | undefined): Language | null {
  const base = (tag ?? '').toLowerCase().split('-')[0] ?? ''
  return (languages as readonly string[]).includes(base) ? (base as Language) : null
}
