import type { Component } from 'vue'
import ApprovalsWidget from './ApprovalsWidget.vue'
import HealthWidget from './HealthWidget.vue'
import KillSwitchWidget from './KillSwitchWidget.vue'
import NotificationsWidget from './NotificationsWidget.vue'
import PrinterWidget from './PrinterWidget.vue'
import RecentRunsWidget from './RecentRunsWidget.vue'
import TasksWidget from './TasksWidget.vue'
import TodayCostWidget from './TodayCostWidget.vue'

/** Dashboard widgets in display order; each reads existing API data. Later features append theirs. */
export const widgets: { id: string; component: Component }[] = [
  { id: 'approvals', component: ApprovalsWidget },
  { id: 'tasks', component: TasksWidget },
  { id: 'notifications', component: NotificationsWidget },
  { id: 'recent-runs', component: RecentRunsWidget },
  { id: 'today-cost', component: TodayCostWidget },
  { id: 'printer', component: PrinterWidget },
  { id: 'health', component: HealthWidget },
  { id: 'kill-switch', component: KillSwitchWidget },
]
