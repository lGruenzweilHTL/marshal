import { AlertTriangle, Bell, Inbox } from 'lucide-vue-next'
import type { Component } from 'vue'
import type { Schemas } from '../api/client'
import type { Tone } from '../ui'

/** Icon and tone per notification priority, the same in every list and on the deep-link screen. */
export const priorityLook: Record<Schemas['NotificationPriority'], { icon: Component; tone: Tone }> = {
  critical: { icon: AlertTriangle, tone: 'danger' },
  normal: { icon: Bell, tone: 'info' },
  low: { icon: Inbox, tone: 'neutral' },
}
