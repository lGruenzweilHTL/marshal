import type { Component } from 'vue'
import HealthWidget from './HealthWidget.vue'
import RecentRunsWidget from './RecentRunsWidget.vue'
import TodayCostWidget from './TodayCostWidget.vue'

/** Dashboard widgets in display order; each reads existing API data. Later features append theirs. */
export const widgets: { id: string; component: Component }[] = [
  { id: 'recent-runs', component: RecentRunsWidget },
  { id: 'today-cost', component: TodayCostWidget },
  { id: 'health', component: HealthWidget },
]
