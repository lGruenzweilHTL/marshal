import { Activity, BookOpen, ClipboardCheck, Cpu, FileCode2, FolderClosed, House, Inbox, LayoutGrid, Mail, MessageSquare, Printer, Share2, ShieldCheck, SlidersHorizontal, SquareCheckBig, Gauge } from 'lucide-vue-next'
import type { Component } from 'vue'

export type GroupId = 'act' | 'activity' | 'memory' | 'connections' | 'settings'

export interface Destination {
  /** Matches `meta.nav` of the routes that belong to it. */
  id: string
  group: GroupId
  icon: Component
  path: string
  /** On the phone tab bar (the rest is in the More sheet). */
  tab?: boolean
  /** Only for users with the `admin` role. */
  admin?: boolean
}

/** The approved information architecture: five groups on desktop, Home · Chat · Inbox · Printer · More on phones. */
export const destinations: Destination[] = [
  { id: 'home', group: 'act', icon: House, path: '/', tab: true },
  { id: 'chat', group: 'act', icon: MessageSquare, path: '/chat', tab: true },
  { id: 'inbox', group: 'act', icon: Inbox, path: '/inbox', tab: true },
  { id: 'printer', group: 'act', icon: Printer, path: '/printers', tab: true },
  { id: 'tasks', group: 'activity', icon: SquareCheckBig, path: '/tasks' },
  { id: 'runs', group: 'activity', icon: Activity, path: '/runs' },
  { id: 'cost', group: 'activity', icon: Gauge, path: '/cost' },
  { id: 'chunks', group: 'memory', icon: BookOpen, path: '/memory' },
  { id: 'projects', group: 'memory', icon: FolderClosed, path: '/memory/projects' },
  { id: 'map', group: 'memory', icon: Share2, path: '/memory/map' },
  { id: 'audit', group: 'memory', icon: ClipboardCheck, path: '/memory/audit' },
  { id: 'mail', group: 'connections', icon: Mail, path: '/mail/accounts' },
  { id: 'preferences', group: 'settings', icon: SlidersHorizontal, path: '/preferences' },
  { id: 'security', group: 'settings', icon: ShieldCheck, path: '/security' },
  { id: 'system', group: 'settings', icon: Cpu, path: '/system' },
  { id: 'config', group: 'settings', icon: FileCode2, path: '/system/config', admin: true },
]

export const groups: GroupId[] = ['act', 'activity', 'memory', 'connections', 'settings']

/** The More tab's icon. */
export const moreIcon = LayoutGrid

export function visibleDestinations(admin: boolean): Destination[] {
  return destinations.filter((destination) => admin || !destination.admin)
}
