import { h } from 'vue'
import { createRouter, createWebHistory, type Router } from 'vue-router'
import type { ApiSession } from './api/client'

/** Router with history under the app's base path. */
export function createAppRouter(_session: ApiSession): Router {
  return createRouter({
    history: createWebHistory(new URL(document.baseURI).pathname),
    routes: [{ path: '/:pathMatch(.*)*', component: { render: () => h('p', 'Marshal') } }],
  })
}
