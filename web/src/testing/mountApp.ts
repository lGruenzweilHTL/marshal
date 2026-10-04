import { createMemoryHistory, type Router } from 'vue-router'
import { createApiSession, type ApiSession } from '../api/client'
import { apiKey } from '../api/inject'
import { createAppI18n, type AppI18n } from '../i18n'
import type { Language } from '../i18n/catalog'
import { createLocale, localeKey, type LocaleStore } from '../i18n/locale'
import { bannerKey, createErrorBanner, type ErrorBanner } from '../layout/banner'
import { createAppRouter } from '../router'
import { createUserSession, sessionKey, type UserSession } from '../session'
import { liveKey } from '../live/liveEvents'
import { createToasts, toastKey, type Toasts } from '../ui/toast'
import { FakeLiveEvents } from './fakeLiveEvents'
import type { FakeServer } from './fakeServer'

export const me = { id: '0199aaaa-0000-7000-8000-000000000001', displayName: 'Lukas', roles: ['user'], scopes: ['conversation:write'] }

/** Global mount options for a component under test: API session on the fake server, router at `path`, English by default. */
export async function mountOptions(server: FakeServer, path: string, options: { language?: Language; timeZone?: string } = {}) {
  const api: ApiSession = createApiSession({ baseUrl: 'https://marshal.test', fetch: server.fetch })
  const user: UserSession = createUserSession(api)
  const i18n: AppI18n = createAppI18n()
  const locale: LocaleStore = createLocale(i18n, { languages: [options.language ?? 'en'], timeZone: options.timeZone ?? 'Europe/Vienna' })
  const banner: ErrorBanner = createErrorBanner()
  const toasts: Toasts = createToasts()
  const router: Router = createAppRouter(user, createMemoryHistory())
  const live = new FakeLiveEvents()
  await router.push(path)
  await router.isReady()
  return {
    api,
    user,
    router,
    live,
    i18n,
    locale,
    banner,
    toasts,
    options: {
      global: {
        plugins: [router, i18n],
        provide: {
          [apiKey as symbol]: api,
          [sessionKey as symbol]: user,
          [liveKey as symbol]: live,
          [localeKey as symbol]: locale,
          [bannerKey as symbol]: banner,
          [toastKey as symbol]: toasts,
        },
      },
    },
  }
}
