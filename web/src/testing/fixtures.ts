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

export function printerState(overrides: Partial<Schemas['PrinterState']> = {}): Schemas['PrinterState'] {
  return {
    printer: 'voron',
    klippyState: 'ready',
    stateMessage: null,
    printState: 'printing',
    filename: 'benchy.gcode',
    progress: 0.25,
    printDuration: 600,
    totalDuration: 720,
    filamentUsed: 812.5,
    currentLayer: 30,
    totalLayers: 120,
    etaSeconds: 1800,
    message: null,
    extruder: { temperature: 200, target: 245, power: 1 },
    bed: { temperature: 100, target: 100, power: 0.5 },
    updatedAt: '2026-09-29T10:00:00Z',
    ...overrides,
  }
}

export function printerSummary(overrides: Partial<Schemas['PrinterSummary']> = {}): Schemas['PrinterSummary'] {
  return { name: 'voron', online: true, state: printerState(), macros: ['PARK'], webUrl: 'http://voron.local/', babysitter: null, ...overrides }
}
