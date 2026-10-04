import { createApp, type Component } from 'vue'
import App from './App.vue'
import { createApiSession } from './api/client'
import { apiKey } from './api/inject'
import { createAppI18n } from './i18n'
import { browserLocale, createLocale, localeKey, type BrowserLocale } from './i18n/locale'
import { bannerKey, createErrorBanner, installGlobalErrorHandlers } from './layout/banner'
import { createLiveEvents, liveKey, type LiveEvents } from './live/liveEvents'
import { createAppRouter } from './router'
import { createUserSession, sessionKey } from './session'
import { applyStoredAppearance } from './ui/appearance'
import { createToasts, toastKey } from './ui/toast'
import './ui/tokens.css'
import './ui/fonts.css'
import './ui/base.css'
import './styles.css'

export interface StartOptions {
  /** The API's fetch (preview mode passes its fake API). */
  fetch?: typeof globalThis.fetch
  /** The live-update stream (preview mode passes a silent one). */
  live?: (baseUrl: string) => LiveEvents
  /** Language and time zone before sign-in. */
  browser?: BrowserLocale
  /** The root component (preview mode wraps the app with its panel). */
  root?: Component
}

/** Creates and mounts the app; the production entry and preview mode share it. */
export function startApp(options: StartOptions = {}) {
  applyStoredAppearance()
  const api = createApiSession({ fetch: options.fetch })
  const user = createUserSession(api)
  const i18n = createAppI18n()
  const locale = createLocale(i18n, options.browser ?? browserLocale())
  const banner = createErrorBanner()
  const router = createAppRouter(user, undefined, () => locale.loadPreferences(api))
  const app = createApp(options.root ?? App)
  app.provide(apiKey, api)
  app.provide(sessionKey, user)
  app.provide(localeKey, locale)
  app.provide(bannerKey, banner)
  app.provide(toastKey, createToasts())
  app.provide(liveKey, (options.live ?? createLiveEvents)(api.baseUrl))
  installGlobalErrorHandlers(app, router, banner)
  app.use(i18n)
  app.use(router)
  app.mount('#app')
  return { app, api, locale, router }
}
