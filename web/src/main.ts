import { createApp } from 'vue'
import App from './App.vue'
import { createApiSession } from './api/client'
import { apiKey } from './api/inject'
import { createAppRouter } from './router'
import './styles.css'

const session = createApiSession()
const app = createApp(App)
app.provide(apiKey, session)
app.use(createAppRouter(session))
app.mount('#app')
