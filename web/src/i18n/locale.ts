import { computed, inject, shallowRef, type ComputedRef, type InjectionKey, type Ref } from 'vue'
import type { Api, ApiResult, Schemas } from '../api/client'
import { createFormatter, setCurrentFormatter, type Formatter } from '../format'
import { supportedLanguage, type Language } from './catalog'
import type { AppI18n } from './index'

/** The interface language and time zone: from the browser before sign-in, from the user's preferences after. */
export interface LocaleStore {
  readonly language: Readonly<Ref<Language>>
  readonly timeZone: Readonly<Ref<string>>
  /** Formatter for the current language and time zone. */
  readonly format: ComputedRef<Formatter>
  /** Loads `/api/v1/me/preferences` and applies them; on failure the current language stays. */
  loadPreferences(api: Api): Promise<ApiResult<Schemas['UserPreferencesResponse']>>
  /** Applies saved preferences right away (the preferences view calls it after a save), no reload. */
  apply(preferences: Pick<Schemas['UserPreferencesResponse'], 'language' | 'timeZone'>): void
}

export interface BrowserLocale {
  languages: readonly string[]
  timeZone: string
}

export const localeKey: InjectionKey<LocaleStore> = Symbol('locale')

export function browserLocale(): BrowserLocale {
  return { languages: navigator.languages, timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone }
}

export function createLocale(i18n: AppI18n, browser: BrowserLocale = browserLocale()): LocaleStore {
  const fromBrowser = browser.languages.map(supportedLanguage).find((language) => language !== null) ?? 'en'
  const language = shallowRef<Language>(fromBrowser)
  const timeZone = shallowRef(browser.timeZone)
  const format = computed(() => createFormatter(language.value, timeZone.value))

  function set(next: Language, zone: string) {
    language.value = next
    timeZone.value = zone
    i18n.global.locale.value = next
    document.documentElement.lang = next
    setCurrentFormatter(format.value)
  }

  set(fromBrowser, browser.timeZone)

  const store: LocaleStore = {
    language,
    timeZone,
    format,
    async loadPreferences(api) {
      const result = await api.call((client) => client.GET('/api/v1/me/preferences'))
      if (result.data) store.apply(result.data)
      return result
    },
    apply(preferences) {
      set(supportedLanguage(preferences.language) ?? 'en', preferences.timeZone || timeZone.value)
    },
  }
  return store
}

export function useLocale(): LocaleStore {
  const locale = inject(localeKey)
  if (!locale) throw new Error('No locale store provided.')
  return locale
}

/** The formatter for the current language and time zone. */
export function useFormat(): ComputedRef<Formatter> {
  return useLocale().format
}
