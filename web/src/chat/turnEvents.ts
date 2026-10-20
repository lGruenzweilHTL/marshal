/**
 * Payloads of the turn endpoint's server-sent events (`delta`, `status`, `artifact`, `reasoning`, `tool`,
 * `input_request`, `widget`, `app`, `final`); not described by OpenAPI. `reasoning`, `tool`, `input_request`, `widget` and `app` come only to the web UI
 * (cookie sessions).
 */
export interface TurnDelta {
  text: string
}

export interface TurnStatus {
  text: string
}

export interface TurnFinal {
  speech: string
  continueConversation: boolean
  /** Marshal's session id; send it with the next turn to continue the conversation. */
  conversationId: string
}

/** An artifact the turn shows (`artifacts_show`). */
export interface TurnArtifact {
  id: string
  name: string
  kind: 'image' | 'svg' | 'video' | 'model3d' | 'code' | 'diff' | 'pdf' | 'markdown' | 'html' | 'other'
  url: string
}

/** A built-in widget a tool call shows (`printer`, `music`); `data` is the widget's starting state. */
export interface TurnWidget {
  kind: string
  toolCallId: string
  data: unknown
}

/** An MCP app a tool call shows; load it from `/api/v1/apps/{appId}`, its HTML is `/mcp-apps/{appId}`. */
export interface TurnApp {
  appId: string
  server: string
  tool: string
  toolCallId: string
}

/** A chunk of the model's reasoning. */
export interface TurnReasoning {
  text: string
}

/** An MCP server asked the user for input; load it from `/api/v1/approvals/inputs/{id}`. */
export interface TurnInputRequest {
  id: string
}

export type ToolState = 'running' | 'ok' | 'error' | 'denied' | 'pending'

/** A tool call started (`running`) or ended; the ending event repeats the id. */
export interface TurnTool {
  id: string
  /** Tool id, e.g. `printer.status`. */
  tool: string
  /** One-line argument summary. */
  arguments: string
  state: ToolState
  durationMs?: number | null
  /** One-line result summary or error code; absent for sensitive tools. */
  result?: string | null
}

/** One line of a reply's timeline: a tool call, or a status message of the turn. */
export type ToolStep = ({ kind: 'tool' } & TurnTool) | { kind: 'status'; text: string }

/** Adds a tool event to the timeline: a new line for a new call, the same line updated when the call ends. */
export function applyTool(steps: ToolStep[], event: TurnTool): void {
  const index = steps.findIndex((step) => step.kind === 'tool' && step.id === event.id)
  if (index < 0) steps.push({ kind: 'tool', ...event })
  else steps[index] = { kind: 'tool', ...event }
}
