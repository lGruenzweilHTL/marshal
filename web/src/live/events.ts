import type { Schemas } from '../api/client'

/**
 * Payloads of the live-update stream (`GET /api/v1/events`), hand-written to match the server records; the stream's
 * OpenAPI description only knows the envelope (`LiveEventMessage`).
 */

/** Envelope of every live event; the SSE event name is `type`. */
export interface LiveMessage<P = unknown> {
  topic: string
  type: string
  id: string
  payload: P
}

/** `run.started` on topic `runs` (`RunStartedPayload`). */
export interface RunStartedPayload {
  runId: string
  source: 'voice' | 'web' | 'task' | 'api'
  trigger: string
  startedAt: string
}

/** `run.completed` on topic `runs` (`RunCompletedPayload`). */
export interface RunCompletedPayload {
  runId: string
  outcome: 'ok' | 'error' | 'backgrounded' | 'cancelled'
  cost: number
  endedAt: string
}

/** `printer.state` on topic `printer`: the printer's new state, null when it went offline. */
export interface PrinterStatePayload {
  printer: string
  state: Schemas['PrinterState'] | null
}

/** `task.changed` on topic `tasks` (`TaskChangedPayload`). */
export interface TaskChangedPayload {
  taskId: string
  name: string | null
  status: Schemas['AgentTaskStatus']
  statusReason: string | null
  nextRunAt: string | null
  resultAt: string | null
}

/** `notification.created` and `notification.updated` on topic `notifications` (`NotificationPayload`). */
export interface NotificationPayload {
  id: string
  source: string
  priority: Schemas['NotificationPriority']
  text: string
  outcome: Schemas['NotificationOutcome']
  channel: string | null
  reason: string | null
  taskId: string | null
  createdAt: string
}

/** Event type → payload. Later changes add their topics' events here. */
export interface LiveEventMap {
  'run.started': RunStartedPayload
  'run.completed': RunCompletedPayload
  'printer.state': PrinterStatePayload
  'task.changed': TaskChangedPayload
  'notification.created': NotificationPayload
  'notification.updated': NotificationPayload
}

export type LiveEventType = keyof LiveEventMap
