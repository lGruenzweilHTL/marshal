import type { Schemas } from '../api/client'

export function runSummary(overrides: Partial<Schemas['RunSummary']> = {}): Schemas['RunSummary'] {
  return {
    id: 'r-1',
    sessionId: 's-1',
    source: 'voice',
    inputMode: 'voice',
    trigger: 'conversation',
    satelliteId: 'assist_satellite.kitchen',
    startedAt: '2026-09-28T08:00:00Z',
    endedAt: '2026-09-28T08:00:02Z',
    outcome: 'ok',
    outcomeReason: null,
    tainted: false,
    cost: 0.0012,
    ...overrides,
  }
}

export function runStep(overrides: Partial<Schemas['RunStepDetail']> = {}): Schemas['RunStepDetail'] {
  return {
    sequence: 1,
    kind: 'reply',
    model: null,
    tier: null,
    tokensIn: null,
    tokensOut: null,
    tokensCached: null,
    firstTokenMs: null,
    durationMs: 0,
    cost: null,
    tool: null,
    riskTier: null,
    guardDecision: null,
    reasons: [],
    input: null,
    content: null,
    arguments: null,
    reference: null,
    continueConversation: null,
    error: null,
    depth: 0,
    notes: [],
    ...overrides,
  }
}

export function health(overrides: { status?: string; warnings?: string[] | null } = {}) {
  return {
    status: overrides.status ?? 'healthy',
    checks: {
      database: { status: 'healthy', description: null, warnings: null },
      secrets: { status: overrides.status ?? 'healthy', description: null, warnings: overrides.warnings ?? null },
    },
  }
}
