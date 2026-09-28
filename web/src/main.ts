import { createApp } from 'vue'
import App from './App.vue'
import { createApiSession } from './api/client'
import { apiKey } from './api/inject'
import { createAppRouter } from './router'
import { createUserSession, sessionKey } from './session'
import './styles.css'

const api = createApiSession()
const user = createUserSession(api)
const app = createApp(App)
app.provide(apiKey, api)
app.provide(sessionKey, user)
app.use(createAppRouter(user))
app.mount('#app')
