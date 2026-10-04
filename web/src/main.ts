import { createApp } from 'vue'
import App from './App.vue'
import { createApiSession } from './api/client'
import { apiKey } from './api/inject'
import { createAppI18n } from './i18n'
import { createLocale, localeKey } from './i18n/locale'
import { bannerKey, createErrorBanner, installGlobalErrorHandlers } from './layout/banner'
import { createLiveEvents, liveKey } from './live/liveEvents'
import { createAppRouter } from './router'
import { createUserSession, sessionKey } from './session'
import { createToasts, toastKey } from './ui/toast'
import './ui/tokens.css'
import './ui/fonts.css'
import './ui/base.css'
import './styles.css'

const api = createApiSession()
const user = createUserSession(api)
const i18n = createAppI18n()
const locale = createLocale(i18n)
const banner = createErrorBanner()
const router = createAppRouter(user, undefined, () => locale.loadPreferences(api))
const app = createApp(App)
app.provide(apiKey, api)
app.provide(sessionKey, user)
app.provide(localeKey, locale)
app.provide(bannerKey, banner)
app.provide(toastKey, createToasts())
app.provide(liveKey, createLiveEvents(api.baseUrl))
installGlobalErrorHandlers(app, router, banner)
app.use(i18n)
app.use(router)
app.mount('#app')
