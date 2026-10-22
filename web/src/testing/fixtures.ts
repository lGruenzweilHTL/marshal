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
    reasoning: null,
    reasoningWithheld: false,
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

export function taskItem(overrides: Partial<Schemas['TaskItem']> = {}): Schemas['TaskItem'] {
  return {
    id: 't-1',
    name: 'briefing',
    goal: 'Morning briefing.',
    kind: 'agent',
    status: 'waiting',
    statusReason: null,
    trigger: { kind: 'cron', spec: '0 6 * * *', once: false },
    nextRunAt: '2026-10-01T04:00:00Z',
    budget: { tokens: null, cost: 0.5, steps: null, wallTimeSeconds: null, runs: null },
    spent: { tokens: 1200, cost: 0.12, steps: 3, wallTimeSeconds: 20, runs: 1 },
    resultSpeech: 'Two new mails.',
    resultAt: '2026-09-30T04:01:00Z',
    parentId: null,
    createdBy: 'user',
    createdAt: '2026-09-28T08:00:00Z',
    updatedAt: '2026-09-30T04:01:00Z',
    ...overrides,
  }
}

export function taskDetail(overrides: Partial<Schemas['TaskDetail']> = {}): Schemas['TaskDetail'] {
  return {
    task: taskItem(),
    notes: 'Mention the parcel.',
    result: { speech: 'Two new mails.', body: '## Mail\n\n**Two** new mails.' },
    deliveries: [{ channel: 'voice', followUp: true, when: ['state(binary_sensor.kitchen_occupancy = on)'], expires: '11:00', fallback: 'push' }],
    overrideQuietHoursUntil: null,
    runs: [runSummary({ id: 'r-9', source: 'task', trigger: 'task' })],
    notifications: [notificationEntry()],
    executorSessions: [],
    toolPolicy: null,
    availableTools: ['agent.extract', 'mail.search', 'mail.read', 'rss.search'],
    preparation: null,
    ...overrides,
  }
}

export function notificationEntry(overrides: Partial<Schemas['NotificationEntry']> = {}): Schemas['NotificationEntry'] {
  return {
    id: 'n-1',
    taskId: 't-1',
    source: 'task:t-1',
    priority: 'normal',
    content: 'Two new mails.',
    channel: 'voice',
    outcome: 'delivered',
    reason: null,
    coalescedInto: null,
    createdAt: '2026-09-30T04:01:00Z',
    deliveredAt: '2026-09-30T04:01:02Z',
    ...overrides,
  }
}

export function approvalItem(overrides: Partial<Schemas['ApprovalItem']> = {}): Schemas['ApprovalItem'] {
  return {
    id: 'a-1',
    taskId: 't-1',
    runId: 'r-1',
    tool: 'lock.unlock',
    arguments: { door: 'front' },
    summary: 'Unlock the front door',
    reasons: [
      { code: 'rule', sentence: 'Unlocking needs your approval.' },
      { code: 'taint', sentence: 'This run read an email, which could contain injected instructions.' },
    ],
    taint: [{ kind: 'mail', firstStep: 2 }],
    createdAt: '2026-09-30T08:00:00Z',
    expiresAt: '2026-09-30T08:10:00Z',
    ...overrides,
  }
}

export function proposal(overrides: Partial<Schemas['ProposalRecord']> = {}): Schemas['ProposalRecord'] {
  return {
    id: 'p-1',
    taskId: 't-2',
    executorSessionId: 's-1',
    kind: 'code',
    target: 'demo',
    branch: 'marshal/abc',
    title: 'Add a feature file',
    diff: null,
    status: 'open',
    createdAt: '2026-09-30T08:00:00Z',
    decidedAt: null,
    ...overrides,
  }
}

/** An artifact (`ArtifactResponse`): a tainted STL from a coding session unless overridden. */
export function artifact(overrides: Record<string, unknown> = {}) {
  return {
    id: 'a1',
    name: 'bracket.stl',
    kind: 'model3d',
    mediaType: 'model/stl',
    size: 1536,
    source: 'executor',
    sourceReference: 'task:t1',
    tainted: true,
    createdAt: '2026-10-08T09:00:00Z',
    url: '/api/v1/artifacts/a1/content',
    ...overrides,
  }
}

export function conversationSummary(overrides: Partial<Schemas['ConversationSummary']> & Pick<Schemas['ConversationSummary'], 'id'>): Schemas['ConversationSummary'] {
  return {
    source: 'web',
    createdAt: '2026-10-12T08:00:00Z',
    lastTurnAt: '2026-10-12T08:00:00Z',
    turns: 1,
    title: 'Hello',
    canContinue: true,
    satelliteId: null,
    areaId: null,
    ...overrides,
  }
}

export function suggestionItem(overrides: Partial<Schemas['SuggestionItem']> = {}): Schemas['SuggestionItem'] {
  return {
    id: 'sg-1',
    detector: 'window_rain',
    text: 'The bedroom window is open and rain is likely within the hour. Close it?',
    snippet: null,
    delivery: 'inbox',
    status: 'open',
    actionSummary: 'Close cover.bedroom_window',
    actionReasons: [],
    createdAt: '2026-09-30T08:00:00Z',
    expiresAt: '2026-09-30T14:00:00Z',
    ...overrides,
  }
}

export function skillProposalSummary(overrides: Partial<Schemas['SkillProposalSummary']> = {}): Schemas['SkillProposalSummary'] {
  return {
    id: 'sp-1',
    name: 'nightly-backup-check',
    description: 'Checks last night\'s backup jobs and reports failures.',
    status: 'open',
    hasDiff: false,
    sourceRunCount: 3,
    createdAt: '2026-09-30T03:10:00Z',
    ...overrides,
  }
}

export function skillProposal(overrides: Partial<Schemas['SkillProposalRecord']> = {}): Schemas['SkillProposalRecord'] {
  return {
    id: 'sp-1',
    name: 'nightly-backup-check',
    description: 'Checks last night\'s backup jobs and reports failures.',
    content: '---\nname: nightly-backup-check\ndescription: Checks last night\'s backup jobs and reports failures.\n---\n\n1. Call `infra.backups`.\n',
    diff: null,
    sourceRuns: ['r-1', 'r-2', 'r-3'],
    status: 'open',
    createdAt: '2026-09-30T03:10:00Z',
    decidedAt: null,
    ...overrides,
  }
}

export function maintenanceItem(overrides: Partial<Schemas['MaintenanceItemStatus']> = {}): Schemas['MaintenanceItemStatus'] {
  return {
    id: 'voron_nozzle',
    name: 'Voron nozzle',
    usage: 182.5,
    every: 200,
    warnBefore: 20,
    unit: 'h',
    state: 'soon',
    lastDoneAt: '2026-06-02T10:00:00Z',
    error: null,
    ...overrides,
  }
}

export function detectorItem(overrides: Partial<Schemas['DetectorItem']> = {}): Schemas['DetectorItem'] {
  return { detector: 'window_rain', sent: 5, accepted: 3, dismissed: 1, estimate: 0.667, muted: false, mutedUntil: null, ...overrides }
}
