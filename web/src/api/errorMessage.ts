import type { ApiError } from './errors'

/** The part of vue-i18n's composer that error messages need. */
export interface Translator {
  t(key: string, named?: Record<string, unknown>): string
  te(key: string): boolean
}

/**
 * The message for a failed call in the interface language: the catalog entry for a known problem `code`
 * (`errors.code.<code>`), else the server's `detail`, else the entry for the status (`errors.status.<status>`,
 * `errors.status.other`) or for the kind (`errors.network`, `errors.unexpected`).
 */
export function errorMessage(error: ApiError, i18n: Translator): string {
  if (error.kind === 'network') return i18n.t('errors.network')
  if (error.kind === 'unexpected') return i18n.t('errors.unexpected')
  if (error.code && i18n.te(`errors.code.${error.code}`)) return i18n.t(`errors.code.${error.code}`)
  if (error.detail) return error.detail
  const status = `errors.status.${error.status}`
  return i18n.te(status) ? i18n.t(status) : i18n.t('errors.status.other', { status: error.status })
}
