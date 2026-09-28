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

/** Event type → payload. Later changes add their topics' events here. */
export interface LiveEventMap {
  'run.started': RunStartedPayload
  'run.completed': RunCompletedPayload
}

export type LiveEventType = keyof LiveEventMap
