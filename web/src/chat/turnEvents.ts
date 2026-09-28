/** Payloads of the turn endpoint's server-sent events (`delta`, `status`, `final`); not described by OpenAPI. */
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
