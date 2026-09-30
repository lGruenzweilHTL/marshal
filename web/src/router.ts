import { createRouter, createWebHistory, type RouteRecordRaw, type Router, type RouterHistory } from 'vue-router'
import LocalLoginPage from './auth/LocalLoginPage.vue'
import LoginPage from './auth/LoginPage.vue'
import SetupPage from './auth/SetupPage.vue'
import AppShell from './layout/AppShell.vue'
import ChatView from './chat/ChatView.vue'
import ConfigView from './config/ConfigView.vue'
import MailAccountsView from './mail/MailAccountsView.vue'
import DashboardView from './dashboard/DashboardView.vue'
import AuditView from './memory/AuditView.vue'
import ChunkEditorView from './memory/ChunkEditorView.vue'
import ChunkListView from './memory/ChunkListView.vue'
import GraphView from './memory/GraphView.vue'
import MemoryLayout from './memory/MemoryLayout.vue'
import ProjectsView from './memory/ProjectsView.vue'
import ScatterView from './memory/ScatterView.vue'
import NotFoundView from './layout/NotFoundView.vue'
import NotificationsView from './notifications/NotificationsView.vue'
import PreferencesView from './preferences/PreferencesView.vue'
import PrinterView from './printers/PrinterView.vue'
import ReviewQueueView from './review/ReviewQueueView.vue'
import CostView from './runs/CostView.vue'
import RunDetailView from './runs/RunDetailView.vue'
import RunsView from './runs/RunsView.vue'
import SecretsView from './secrets/SecretsView.vue'
import SecurityView from './security/SecurityView.vue'
import SystemView from './system/SystemView.vue'
import TaskDetailView from './tasks/TaskDetailView.vue'
import TasksView from './tasks/TasksView.vue'
import type { UserSession } from './session'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without a session (login and setup). */
    public?: boolean
    /** Page title for the browser tab. */
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
      { path: 'tasks', name: 'tasks', component: TasksView, meta: { title: 'Tasks' } },
      { path: 'tasks/:id', name: 'task', component: TaskDetailView, meta: { title: 'Task' } },
      { path: 'review/:id?', name: 'review', component: ReviewQueueView, meta: { title: 'Review' } },
      { path: 'notifications', name: 'notifications', component: NotificationsView, meta: { title: 'Notifications' } },
      { path: 'runs', name: 'runs', component: RunsView, meta: { title: 'Runs' } },
      { path: 'runs/:id', name: 'run', component: RunDetailView, meta: { title: 'Run' } },
      { path: 'cost', name: 'cost', component: CostView, meta: { title: 'Cost' } },
      { path: 'mail/accounts', name: 'mail-accounts', component: MailAccountsView, meta: { title: 'Mail accounts' } },
      { path: 'printers/:name?', name: 'printers', component: PrinterView, meta: { title: 'Printer' } },
      {
        path: 'memory',
        component: MemoryLayout,
        children: [
          { path: '', name: 'memory', component: ChunkListView, meta: { title: 'Memory' } },
          { path: 'new', name: 'memory-new', component: ChunkEditorView, meta: { title: 'New memory' } },
          { path: 'chunks/:id', name: 'memory-chunk', component: ChunkEditorView, meta: { title: 'Memory' } },
          { path: 'projects', name: 'memory-projects', component: ProjectsView, meta: { title: 'Projects' } },
          { path: 'audit', name: 'memory-audit', component: AuditView, meta: { title: 'Memory audit' } },
          { path: 'graph', name: 'memory-graph', component: GraphView, meta: { title: 'Memory graph' } },
          { path: 'scatter', name: 'memory-scatter', component: ScatterView, meta: { title: 'Memory scatter' } },
        ],
      },
      { path: 'security', name: 'security', component: SecurityView, meta: { title: 'Security' } },
      { path: 'secrets', name: 'secrets', component: SecretsView, meta: { title: 'Secrets' } },
      { path: 'preferences', name: 'preferences', component: PreferencesView, meta: { title: 'Preferences' } },
      { path: 'system', name: 'system', component: SystemView, meta: { title: 'System' } },
      { path: 'config', name: 'config', component: ConfigView, meta: { title: 'Configuration' } },
      { path: ':pathMatch(.*)*', name: 'not-found', component: NotFoundView, meta: { title: 'Not found' } },
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
  router.afterEach((to) => {
    document.title = to.meta.title ? `${to.meta.title} · Marshal` : 'Marshal'
  })
  return router
}
