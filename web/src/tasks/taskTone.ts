import type { Schemas } from '../api/client'
import type { Tone } from '../ui'

export const statusTone: Record<Schemas['AgentTaskStatus'], Tone> = {
  pending: 'neutral',
  running: 'info',
  waiting: 'warning',
  done: 'success',
  failed: 'danger',
  cancelled: 'neutral',
}
