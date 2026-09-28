import { createRouter, createWebHistory, type RouteRecordRaw, type Router, type RouterHistory } from 'vue-router'
import LocalLoginPage from './auth/LocalLoginPage.vue'
import LoginPage from './auth/LoginPage.vue'
import SetupPage from './auth/SetupPage.vue'
import AppShell from './layout/AppShell.vue'
import ChatView from './chat/ChatView.vue'
import DashboardView from './dashboard/DashboardView.vue'
import PlaceholderView from './layout/PlaceholderView.vue'
import CostView from './runs/CostView.vue'
import RunDetailView from './runs/RunDetailView.vue'
import RunsView from './runs/RunsView.vue'
import SystemView from './system/SystemView.vue'
import type { UserSession } from './session'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without a session (login and setup). */
    public?: boolean
    /** Title shown in the shell header. */
    title?: string
  }
}

const routes: RouteRecordRaw[] = [
  { path: '/login', component: LoginPage, meta: { public: true } },
  { path: '/login/:provider', component: LocalLoginPage, meta: { public: true } },
  { path: '/setup', component: SetupPage, meta: { public: true } },
  {
    path: '/',
    component: AppShell,
    children: [
      { path: '', name: 'dashboard', component: DashboardView, meta: { title: 'Dashboard' } },
      { path: 'chat', name: 'chat', component: ChatView, meta: { title: 'Chat' } },
      { path: 'runs', name: 'runs', component: RunsView, meta: { title: 'Runs' } },
      { path: 'runs/:id', name: 'run', component: RunDetailView, meta: { title: 'Run' } },
      { path: 'cost', name: 'cost', component: CostView, meta: { title: 'Cost' } },
      { path: 'security', name: 'security', component: PlaceholderView, meta: { title: 'Security' } },
      { path: 'secrets', name: 'secrets', component: PlaceholderView, meta: { title: 'Secrets' } },
      { path: 'preferences', name: 'preferences', component: PlaceholderView, meta: { title: 'Preferences' } },
      { path: 'system', name: 'system', component: SystemView, meta: { title: 'System' } },
      { path: ':pathMatch(.*)*', name: 'not-found', component: PlaceholderView, meta: { title: 'Not found' } },
    ],
  },
]

/** App router; views other than login and setup need a session and send anonymous users to the login page. */
export function createAppRouter(user: UserSession, history: RouterHistory = createWebHistory(new URL(document.baseURI).pathname)): Router {
  const router = createRouter({ history, routes })
  router.beforeEach(async (to) => {
    if (to.meta.public) return true
    return (await user.load()) ? true : { path: '/login', query: { returnUrl: to.fullPath } }
  })
  return router
}
