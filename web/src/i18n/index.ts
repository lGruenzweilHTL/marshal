import { createI18n, type I18n } from 'vue-i18n'
import type { Language } from './catalog'
import { de, en, type MessageSchema } from './messages'

declare module 'vue-i18n' {
  // Typed keys for `t()` in templates and `useI18n()`.
  export interface DefineLocaleMessage extends MessageSchema {}
}

export type AppI18n = I18n<{ en: MessageSchema; de: MessageSchema }, {}, {}, Language, false>

/** The app's vue-i18n instance (Composition API mode), English as fallback. */
export function createAppI18n(language: Language = 'en'): AppI18n {
  return createI18n<[MessageSchema], Language, false>({
    legacy: false,
    locale: language,
    fallbackLocale: 'en',
    messages: { en, de },
    missingWarn: false,
    fallbackWarn: false,
  }) as AppI18n
}
