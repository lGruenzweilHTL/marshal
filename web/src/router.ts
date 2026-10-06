import { createRouter, createWebHistory, type RouteRecordRaw, type Router, type RouterHistory } from 'vue-router'
import LocalLoginPage from './auth/LocalLoginPage.vue'
import LoginPage from './auth/LoginPage.vue'
import SetupPage from './auth/SetupPage.vue'
import AppShell from './layout/AppShell.vue'
import ChatView from './chat/ChatView.vue'
import ConfigView from './config/ConfigView.vue'
import HomeView from './home/HomeView.vue'
import ApprovalView from './inbox/ApprovalView.vue'
import InboxView from './inbox/InboxView.vue'
import NotificationView from './inbox/NotificationView.vue'
import ProposalView from './inbox/ProposalView.vue'
import MailAccountsView from './mail/MailAccountsView.vue'
import AuditView from './memory/AuditView.vue'
import ChunkEditorView from './memory/ChunkEditorView.vue'
import ChunkListView from './memory/ChunkListView.vue'
import MapView from './memory/MapView.vue'
import MemoryLayout from './memory/MemoryLayout.vue'
import ProjectsView from './memory/ProjectsView.vue'
import NotFoundView from './layout/NotFoundView.vue'
import PreferencesView from './preferences/PreferencesView.vue'
import PrinterView from './printers/PrinterView.vue'
import CostView from './runs/CostView.vue'
import RunDetailView from './runs/RunDetailView.vue'
import RunsView from './runs/RunsView.vue'
import SecretsView from './secrets/SecretsView.vue'
import SecurityView from './security/SecurityView.vue'
import SystemView from './system/SystemView.vue'
import TaskDetailView from './tasks/TaskDetailView.vue'
import TasksView from './tasks/TasksView.vue'
import SessionErrorPage from './layout/SessionErrorPage.vue'
import type { Me, UserSession } from './session'

declare module 'vue-router' {
  interface RouteMeta {
    /** Reachable without a session (login and setup). */
    public?: boolean
    /** Catalog key of the page title for the browser tab. */
    title?: string
    /** Navigation destination the page belongs to (`layout/navigation.ts`). */
    nav?: string
    /** Not rebuilt on the design system yet: rendered inside `.legacy` with the old styles (removed by task 13.2). */
    legacy?: boolean
  }
}

const routes: RouteRecordRaw[] = [
  { path: '/login', component: LoginPage, meta: { public: true } },
  { path: '/login/:provider', component: LocalLoginPage, meta: { public: true } },
  { path: '/setup', component: SetupPage, meta: { public: true } },
  { path: '/unavailable', name: 'unavailable', component: SessionErrorPage, meta: { public: true } },
  {
    path: '/',
    component: AppShell,
    children: [
      // Act
      { path: '', name: 'home', component: HomeView, meta: { title: 'shell.nav.home', nav: 'home' } },
      { path: 'chat', name: 'chat', component: ChatView, meta: { title: 'shell.nav.chat', nav: 'chat' } },
      { path: 'inbox', name: 'inbox', component: InboxView, meta: { title: 'shell.nav.inbox', nav: 'inbox' } },
      { path: 'inbox/approvals/:id', name: 'approval', component: ApprovalView, meta: { title: 'inbox.approval', nav: 'inbox' } },
      { path: 'inbox/proposals/:id', name: 'proposal', component: ProposalView, meta: { title: 'inbox.proposals', nav: 'inbox' } },
      { path: 'inbox/notifications/:id', name: 'notification', component: NotificationView, meta: { title: 'inbox.notification.title', nav: 'inbox' } },
      { path: 'printers/:name?', name: 'printers', component: PrinterView, meta: { title: 'shell.nav.printer', nav: 'printer' } },
      // Activity
      { path: 'tasks', name: 'tasks', component: TasksView, meta: { title: 'shell.nav.tasks', nav: 'tasks' } },
      { path: 'tasks/:id', name: 'task', component: TaskDetailView, meta: { title: 'shell.nav.tasks', nav: 'tasks' } },
      { path: 'runs', name: 'runs', component: RunsView, meta: { title: 'shell.nav.runs', nav: 'runs' } },
      { path: 'runs/:id', name: 'run', component: RunDetailView, meta: { title: 'shell.nav.runs', nav: 'runs' } },
      { path: 'cost', name: 'cost', component: CostView, meta: { title: 'shell.nav.cost', nav: 'cost' } },
      // Memory
      {
        path: 'memory',
        component: MemoryLayout,
        children: [
          { path: '', name: 'memory', component: ChunkListView, meta: { title: 'shell.nav.chunks', nav: 'chunks' } },
          { path: 'new', name: 'memory-new', component: ChunkEditorView, meta: { title: 'shell.nav.chunks', nav: 'chunks' } },
          { path: 'chunks/:id', name: 'memory-chunk', component: ChunkEditorView, meta: { title: 'shell.nav.chunks', nav: 'chunks' } },
          { path: 'projects', name: 'memory-projects', component: ProjectsView, meta: { title: 'shell.nav.projects', nav: 'projects' } },
          { path: 'map', name: 'memory-map', component: MapView, meta: { title: 'shell.nav.map', nav: 'map' } },
          { path: 'audit', name: 'memory-audit', component: AuditView, meta: { title: 'shell.nav.audit', nav: 'audit' } },
        ],
      },
      // Connections and settings
      { path: 'mail/accounts', name: 'mail-accounts', component: MailAccountsView, meta: { title: 'shell.nav.mail', nav: 'mail' } },
      { path: 'preferences', name: 'preferences', component: PreferencesView, meta: { title: 'shell.nav.preferences', nav: 'preferences' } },
      { path: 'security', name: 'security', component: SecurityView, meta: { title: 'shell.nav.security', nav: 'security' } },
      { path: 'security/secrets', name: 'secrets', component: SecretsView, meta: { title: 'shell.nav.security', nav: 'security' } },
      { path: 'system', name: 'system', component: SystemView, meta: { title: 'shell.nav.system', nav: 'system' } },
      { path: 'system/config', name: 'config', component: ConfigView, meta: { title: 'shell.nav.config', nav: 'config' } },
      // Paths of the previous layout; push notifications already sent still link to some of them.
      { path: 'review/:id?', redirect: (to) => (to.params.id ? `/inbox/approvals/${String(to.params.id)}` : '/inbox') },
      { path: 'notifications', redirect: '/inbox' },
      { path: 'secrets', redirect: '/security/secrets' },
      { path: 'config', redirect: '/system/config' },
      { path: 'memory/graph', redirect: '/memory/map' },
      { path: 'memory/scatter', redirect: { path: '/memory/map', query: { view: 'scatter' } } },
      { path: ':pathMatch(.*)*', name: 'not-found', component: NotFoundView, meta: { title: 'shell.notFound.title' } },
    ],
  },
]

/** App router; views other than login and setup need a session and send anonymous users to the login page. */
export function createAppRouter(
  user: UserSession,
  history: RouterHistory = createWebHistory(new URL(document.baseURI).pathname),
  /** Runs once per signed-in user before the first protected page (the locale store loads the preferences). */
  onSignedIn?: (me: Me) => Promise<unknown>,
  /** Translates the `meta.title` keys for the browser tab. */
  translate?: (key: string) => string,
): Router {
  const router = createRouter({ history, routes })
  let signedIn: string | null = null
  router.beforeEach(async (to) => {
    if (to.meta.public) return true
    const result = await user.load()
    if (result.error) return { name: 'unavailable', query: { returnUrl: to.fullPath } }
    if (!result.data) return { path: '/login', query: { returnUrl: to.fullPath } }
    if (signedIn !== result.data.id) {
      signedIn = result.data.id
      await onSignedIn?.(result.data)
    }
    return true
  })
  user.onExpired(() => {
    const current = router.currentRoute.value
    if (current.meta.public) return
    void router.push({ path: '/login', query: { reason: 'expired', returnUrl: current.fullPath } })
  })
  router.afterEach((to) => {
    document.title = to.meta.title && translate ? `${translate(to.meta.title)} · Marshal` : 'Marshal'
  })
  return router
}
