/** Payloads of the turn endpoint's server-sent events (`delta`, `status`, `artifact`, `final`); not described by OpenAPI. */
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
