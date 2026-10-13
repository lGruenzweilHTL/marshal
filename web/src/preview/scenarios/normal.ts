import type { Schemas } from '../../api/client'
import type { Fixtures, GetData } from './fixtures'

/** The instant the sample data is written around; the preview moves every timestamp by `now - anchor`. */
export const anchor = Date.parse('2026-10-01T09:00:00Z')

/** An ISO timestamp `minutes` from the anchor (negative: before). */
export const at = (minutes: number) => new Date(anchor + minutes * 60_000).toISOString()

const id = (group: number, n: number) => `0199cccc-0000-7000-8000-${String(group).padStart(4, '0')}${String(n).padStart(8, '0')}`

export const ids = {
  owner: id(1, 1),
  printTask: id(3, 1),
  briefing: id(3, 2),
  rssDigest: id(3, 3),
  filamentWatch: id(3, 4),
  backupCheck: id(3, 5),
  approvalLock: id(4, 1),
  approvalPrint: id(4, 2),
  proposal: id(8, 1),
  voron: id(7, 1),
  garden: id(7, 2),
  run: (n: number) => id(9, n),
  conversation: (n: number) => id(10, n),
  chunk: (n: number) => id(6, n),
  notification: (n: number) => id(5, n),
}

export const me: Schemas['MeResponse'] = {
  id: ids.owner,
  displayName: 'Lukas',
  roles: ['admin'],
  scopes: ['conversation:write', 'tasks:read', 'tasks:write', 'memory:read', 'memory:write', 'printers:read', 'printers:control', 'mail:read', 'approvals:write', 'admin'],
  demo: { enabled: false, showBadge: true },
}

export const preferences: Schemas['UserPreferencesResponse'] = {
  language: 'en',
  timeZone: 'Europe/Vienna',
  quietHours: [
    { day: 'monday', start: '22:30:00', end: '06:30:00' },
    { day: 'tuesday', start: '22:30:00', end: '06:30:00' },
    { day: 'wednesday', start: '22:30:00', end: '06:30:00' },
    { day: 'thursday', start: '22:30:00', end: '06:30:00' },
    { day: 'friday', start: '23:30:00', end: '08:00:00' },
    { day: 'saturday', start: '23:30:00', end: '08:00:00' },
    { day: 'sunday', start: '22:30:00', end: '06:30:00' },
  ],
  notificationTargets: { person: 'person.lukas', push: ['notify.mobile_app_pixel_9'] },
  dndUntil: null,
}

const heater = (temperature: number, target: number, power: number): Schemas['HeaterState'] => ({ temperature, target, power })

export const printerState: Schemas['PrinterState'] = {
  printer: 'voron',
  klippyState: 'ready',
  stateMessage: 'Printer is ready',
  printState: 'printing',
  filename: 'benchy_petg_0.2mm.gcode',
  progress: 0.42,
  printDuration: 3120,
  totalDuration: 3300,
  filamentUsed: 4210,
  currentLayer: 112,
  totalLayers: 268,
  etaSeconds: 4320,
  message: null,
  extruder: heater(244.8, 245, 0.62),
  bed: heater(84.9, 85, 0.41),
  updatedAt: at(-1),
}

const approvals: Schemas['ApprovalItem'][] = [
  {
    id: ids.approvalLock,
    taskId: ids.backupCheck,
    runId: ids.run(7),
    tool: 'ha.lock_unlock',
    arguments: { entity_id: 'lock.front_door' },
    summary: 'Unlock the front door',
    reasons: [{ code: 'rule', sentence: 'Unlocking a lock is confirm-tier (risk rule "locks").' }],
    taint: [],
    createdAt: at(-1),
    expiresAt: at(4),
  },
  {
    id: ids.approvalPrint,
    taskId: ids.printTask,
    runId: ids.run(6),
    tool: 'printer.start_print',
    arguments: { printer: 'voron', file: 'gridfinity_bin_2x3.gcode' },
    summary: 'Start gridfinity_bin_2x3.gcode on voron',
    reasons: [
      { code: 'rule', sentence: 'Starting a print is confirm-tier (risk rule "printer.start").' },
      { code: 'taint', sentence: 'This run read an e-mail, so its actions need your confirmation.' },
    ],
    taint: [{ kind: 'mail', firstStep: 3 }],
    createdAt: at(-6),
    expiresAt: at(2),
  },
]

const budget = (cost: number, runs: number): Schemas['TaskBudgetView'] => ({ tokens: 400_000, cost, steps: 60, wallTimeSeconds: 3600, runs })
const spent = (cost: number, runs: number, tokens: number): Schemas['TaskSpendView'] => ({ tokens, cost, steps: runs * 4, wallTimeSeconds: runs * 41, runs })

const tasks: Schemas['TaskItem'][] = [
  {
    id: ids.printTask,
    name: 'Print babysitter',
    goal: 'Watch the Voron print and pause on spaghetti or layer shifts.',
    kind: 'agent',
    status: 'running',
    statusReason: null,
    trigger: { kind: 'event', spec: 'printer.print_started', once: false },
    nextRunAt: null,
    budget: budget(0.5, 40),
    spent: spent(0.0812, 9, 41_250),
    resultSpeech: null,
    resultAt: null,
    parentId: null,
    createdBy: 'system',
    createdAt: at(-52),
    updatedAt: at(-1),
  },
  {
    id: ids.briefing,
    name: 'Morning briefing',
    goal: 'Brief Lukas at 07:00 with weather, calendar, unread mail and printer status.',
    kind: 'agent',
    status: 'waiting',
    statusReason: 'Next run at 07:00.',
    trigger: { kind: 'cron', spec: '0 7 * * *', once: false },
    nextRunAt: at(22 * 60),
    budget: budget(0.2, 1),
    spent: spent(0.0421, 1, 18_400),
    resultSpeech: 'Good morning. 14 degrees and cloudy, two meetings, and the Voron finished overnight.',
    resultAt: at(-120),
    parentId: null,
    createdBy: 'user',
    createdAt: at(-14 * 24 * 60),
    updatedAt: at(-120),
  },
  {
    id: ids.rssDigest,
    name: 'RSS digest',
    goal: 'Summarize unread FreshRSS articles about 3D printing and Home Assistant each evening.',
    kind: 'agent',
    status: 'pending',
    statusReason: null,
    trigger: { kind: 'cron', spec: '30 19 * * *', once: false },
    nextRunAt: at(9 * 60 + 30),
    budget: budget(0.3, 1),
    spent: spent(0, 0, 0),
    resultSpeech: null,
    resultAt: null,
    parentId: null,
    createdBy: 'user',
    createdAt: at(-3 * 24 * 60),
    updatedAt: at(-3 * 24 * 60),
  },
  {
    id: ids.filamentWatch,
    name: 'Filament stock',
    goal: 'Tell me when a spool in the dry box drops below 150 g.',
    kind: 'agent',
    status: 'done',
    statusReason: null,
    trigger: { kind: 'event', spec: 'sensor.drybox_spool_weight', once: false },
    nextRunAt: null,
    budget: budget(0.1, 5),
    spent: spent(0.0123, 2, 6_100),
    resultSpeech: 'The PETG black spool is at 130 grams.',
    resultAt: at(-26 * 60),
    parentId: null,
    createdBy: 'user',
    createdAt: at(-9 * 24 * 60),
    updatedAt: at(-26 * 60),
  },
  {
    id: ids.backupCheck,
    name: 'Weekly backup check',
    goal: 'Check that the Home Assistant backup ran this week and report the size.',
    kind: 'system',
    status: 'failed',
    statusReason: 'Budget exceeded: 5 of 5 runs used.',
    trigger: { kind: 'cron', spec: '0 9 * * 1', once: false },
    nextRunAt: at(5 * 24 * 60),
    budget: budget(0.05, 5),
    spent: spent(0.05, 5, 22_000),
    resultSpeech: null,
    resultAt: null,
    parentId: null,
    createdBy: 'system',
    createdAt: at(-30 * 24 * 60),
    updatedAt: at(-40),
  },
]

const runs: Schemas['RunSummary'][] = [
  { id: ids.run(1), sessionId: id(10, 1), source: 'voice', inputMode: 'voice', trigger: 'conversation', satelliteId: 'assist_satellite.kitchen', startedAt: at(-3), endedAt: at(-3), outcome: 'ok', outcomeReason: null, tainted: false, cost: 0.0012 },
  { id: ids.run(2), sessionId: id(10, 2), source: 'web', inputMode: 'text', trigger: 'conversation', satelliteId: null, startedAt: at(-18), endedAt: at(-18), outcome: 'ok', outcomeReason: null, tainted: false, cost: 0.0094 },
  { id: ids.run(3), sessionId: null, source: 'task', inputMode: null, trigger: 'event', satelliteId: null, startedAt: at(-24), endedAt: at(-23), outcome: 'ok', outcomeReason: null, tainted: false, cost: 0.0031 },
  { id: ids.run(4), sessionId: id(10, 3), source: 'voice', inputMode: 'voice', trigger: 'conversation', satelliteId: 'assist_satellite.living_room', startedAt: at(-41), endedAt: at(-41), outcome: 'backgrounded', outcomeReason: 'Latency cap reached (8 s).', tainted: false, cost: 0.0023 },
  { id: ids.run(5), sessionId: null, source: 'task', inputMode: null, trigger: 'cron', satelliteId: null, startedAt: at(-40), endedAt: at(-40), outcome: 'error', outcomeReason: 'Budget exceeded.', tainted: false, cost: 0.0102 },
  { id: ids.run(6), sessionId: null, source: 'task', inputMode: null, trigger: 'event', satelliteId: null, startedAt: at(-7), endedAt: at(-6), outcome: 'ok', outcomeReason: null, tainted: true, cost: 0.0187 },
  { id: ids.run(7), sessionId: null, source: 'task', inputMode: null, trigger: 'cron', satelliteId: null, startedAt: at(-2), endedAt: at(-1), outcome: 'ok', outcomeReason: null, tainted: false, cost: 0.0044 },
  { id: ids.run(8), sessionId: null, source: 'api', inputMode: 'text', trigger: 'conversation', satelliteId: null, startedAt: at(-95), endedAt: at(-95), outcome: 'ok', outcomeReason: null, tainted: false, cost: 0.0007 },
]

type ConversationEntry = Schemas['ConversationDetail']['entries'][number]

const turn = (n: number, minutes: number, input: string, reply: string | null, overrides: Partial<Extract<ConversationEntry, { kind?: 'turn' }>> = {}): ConversationEntry => ({
  kind: 'turn',
  runId: ids.run(n),
  at: at(minutes),
  inputMode: 'text',
  satelliteId: null,
  areaId: null,
  input,
  reply,
  continueConversation: false,
  artifacts: [],
  outcome: 'ok',
  reasoning: null,
  reasoningWithheld: false,
  tools: [],
  ...overrides,
})

const conversation = (n: number, source: Schemas['SessionSource'], minutes: number, turns: number, title: string, overrides: Partial<Schemas['ConversationSummary']> = {}): Schemas['ConversationSummary'] => ({
  id: id(10, n),
  source,
  createdAt: at(minutes - 5),
  lastTurnAt: at(minutes),
  turns,
  title,
  canContinue: source === 'web',
  satelliteId: null,
  areaId: null,
  ...overrides,
})

const conversations: Schemas['ConversationSummary'][] = [
  conversation(1, 'voice', -3, 1, 'Is the Voron done yet?', { satelliteId: 'assist_satellite.kitchen', areaId: 'kitchen' }),
  conversation(2, 'web', -18, 3, 'Which filament for the garden planter brackets?'),
  conversation(3, 'voice', -41, 1, 'Check all three printers and tell me which one is free', { satelliteId: 'assist_satellite.living_room', areaId: 'living_room' }),
  conversation(4, 'api', -95, 1, 'Summarize the RSS digest for the dashboard'),
  conversation(5, 'web', -26 * 60, 2, 'Draft a reply to the filament invoice from Prusament'),
]

/** Conversation details by session id; the preview serves the one asked for. */
export const conversationDetails: Record<string, Schemas['ConversationDetail']> = {
  [id(10, 1)]: {
    conversation: conversations[0]!,
    entries: [turn(1, -3, 'Is the Voron done yet?', 'Not yet: layer 112 of 268, about 1 h 12 min left.', { inputMode: 'voice', satelliteId: 'assist_satellite.kitchen', areaId: 'kitchen' })],
  },
  [id(10, 2)]: {
    conversation: conversations[1]!,
    entries: [
      turn(20, -26, 'Which filament for the garden planter brackets?', 'PETG: it handles sun and rain better than PLA. You have about **640 g** of black PETG left.', {
        reasoning: 'Outdoor parts need UV and heat resistance, so PLA is out. Check what the user said about filament before and what is in stock.',
        tools: [
          { tool: 'memory.recall', arguments: 'query=filament outdoor parts', state: 'ok', durationMs: 96, result: 'Prefers PETG for outdoor parts' },
          { tool: 'printer.status', arguments: 'printer=voron', state: 'ok', durationMs: 211, result: 'Idle, spool: black PETG, 640 g left' },
        ],
      }),
      turn(21, -22, 'Is that enough for six brackets?', 'Yes. Six brackets take about 210 g with 4 walls and 30 % infill.'),
      turn(2, -18, 'Queue them on the Voron after the current print', 'Should I add six `planter_bracket.gcode` jobs to the Voron queue?', {
        continueConversation: true,
        tools: [{ tool: 'printer.start_print', arguments: 'printer=voron, file=planter_bracket.gcode, copies=6', state: 'pending', durationMs: 0, result: null }],
      }),
    ],
  },
  [id(10, 3)]: {
    conversation: conversations[2]!,
    entries: [
      turn(4, -41, 'Check all three printers and tell me which one is free', "I'm working on it and will tell you when I'm done.", { inputMode: 'voice', satelliteId: 'assist_satellite.living_room', areaId: 'living_room', outcome: 'backgrounded' }),
      { kind: 'backgroundResult', taskId: ids.printTask, at: at(-39), body: 'The Prusa MK4 is free. The Voron is printing (about 1 h left) and the Bambu reports a clogged nozzle.' },
    ],
  },
  [id(10, 4)]: {
    conversation: conversations[3]!,
    entries: [turn(8, -95, 'Summarize the RSS digest for the dashboard', 'Three new posts: Klipper 0.13 is out, a PETG drying guide and a Home Assistant release note.')],
  },
  [id(10, 5)]: {
    conversation: conversations[4]!,
    entries: [
      turn(50, -26 * 60 - 4, 'Draft a reply to the filament invoice from Prusament', 'I saved a draft in your mail: thanks for the invoice, payment follows this week.'),
      turn(51, -26 * 60, 'Thanks', null, { outcome: 'error' }),
    ],
  },
}

const step = (overrides: Partial<Schemas['RunStepDetail']> & Pick<Schemas['RunStepDetail'], 'sequence' | 'kind'>): Schemas['RunStepDetail'] => ({
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
})

const runDetail: Schemas['RunDetail'] = {
  run: runs[5]!,
  language: 'en',
  taintSources: [{ kind: 'mail', firstStep: 3 }],
  steps: [
    step({
      sequence: 1,
      kind: 'context',
      durationMs: 42,
      notes: [
        { kind: 'skill', label: 'printing', detail: 'matched "print"' },
        { kind: 'memory', label: 'Prefers PETG for outdoor parts', score: 0.82 },
        { kind: 'project', label: 'Voron build', detail: 'active' },
      ],
    }),
    step({ sequence: 2, kind: 'modelCall', model: 'qwen3:14b', tier: 'standard', tokensIn: 3_412, tokensOut: 96, tokensCached: 2_048, firstTokenMs: 640, durationMs: 1_820, cost: 0.0041 , reasoning: 'A new model on Printables matches the watch rule. Search the mail for the notification to get the file name.' }),
    step({ sequence: 3, kind: 'toolCall', tool: 'mail.search', riskTier: 'safe', guardDecision: 'safe', arguments: '{"query":"from:printables.com newer_than:1d"}', reference: 'tool-result:mail.search/run-6/3', durationMs: 910 }),
    step({ sequence: 4, kind: 'modelCall', model: 'qwen3:14b', tier: 'standard', tokensIn: 4_980, tokensOut: 142, tokensCached: 3_400, firstTokenMs: 702, durationMs: 2_310, cost: 0.0061 , reasoningWithheld: true }),
    step({
      sequence: 5,
      kind: 'toolCall',
      tool: 'printer.start_print',
      riskTier: 'confirm',
      guardDecision: 'escalatedByTaint',
      reasons: [
        { code: 'rule', sentence: 'Starting a print is confirm-tier (risk rule "printer.start").' },
        { code: 'taint', sentence: 'This run read an e-mail, so its actions need your confirmation.' },
      ],
      arguments: '{"printer":"voron","file":"gridfinity_bin_2x3.gcode"}',
      content: 'Queued for approval.',
      durationMs: 12,
    }),
    step({ sequence: 6, kind: 'reply', content: 'I asked you to approve starting gridfinity_bin_2x3.gcode on the Voron.', continueConversation: false }),
  ],
}

const days: Schemas['DailyCost'][] = Array.from({ length: 14 }, (_, index) => {
  const fast = [0.008, 0.011, 0.006, 0.014, 0.009, 0.012, 0.01][index % 7]!
  const standard = [0.031, 0.044, 0.022, 0.061, 0.038, 0.052, 0.029][index % 7]!
  const advisor = index % 4 === 2 ? 0.084 : index % 5 === 0 ? 0.031 : 0
  const date = new Date(anchor - (13 - index) * 86_400_000).toISOString().slice(0, 10)
  return { date, total: fast + standard + advisor, byTier: { fast, standard }, advisor }
})
const sum = (pick: (day: Schemas['DailyCost']) => number) => Math.round(days.reduce((total, day) => total + pick(day), 0) * 10_000) / 10_000

const cost: Schemas['CostReport'] = {
  timeZone: 'Europe/Vienna',
  from: days[0]!.date,
  to: days.at(-1)!.date,
  days,
  total: sum((day) => day.total),
  byTier: { fast: sum((day) => day.byTier.fast ?? 0), standard: sum((day) => day.byTier.standard ?? 0) },
  advisor: sum((day) => day.advisor),
}

const notifications: Schemas['NotificationEntry'][] = [
  { id: ids.notification(1), taskId: ids.printTask, source: 'print-babysitter', priority: 'critical', content: 'Possible layer shift on the Voron at layer 112. The print continues; check the snapshot.', channel: 'push', outcome: 'delivered', reason: null, coalescedInto: null, createdAt: at(-12), deliveredAt: at(-12) },
  { id: ids.notification(2), taskId: ids.briefing, source: 'morning-briefing', priority: 'normal', content: 'Good morning. 14 °C and cloudy, two meetings today, and the Voron finished overnight.', channel: 'voice', outcome: 'delivered', reason: null, coalescedInto: null, createdAt: at(-120), deliveredAt: at(-120) },
  { id: ids.notification(3), taskId: ids.filamentWatch, source: 'filament-stock', priority: 'normal', content: 'The PETG black spool is at 130 g.', channel: 'push', outcome: 'held', reason: 'Quiet hours until 06:30.', coalescedInto: null, createdAt: at(-26 * 60), deliveredAt: null },
  { id: ids.notification(4), taskId: ids.rssDigest, source: 'rss-digest', priority: 'low', content: 'Three new articles about Klipper input shaping.', channel: null, outcome: 'inboxed', reason: 'Low priority goes to the inbox.', coalescedInto: null, createdAt: at(-15 * 60), deliveredAt: null },
  { id: ids.notification(5), taskId: ids.backupCheck, source: 'backup-check', priority: 'normal', content: 'The weekly backup check stopped: budget exceeded.', channel: 'push', outcome: 'delivered', reason: null, coalescedInto: null, createdAt: at(-40), deliveredAt: at(-40) },
]

const chunk = (n: number, overrides: Partial<Schemas['MemoryChunk']> & Pick<Schemas['MemoryChunk'], 'type' | 'title' | 'content'>): Schemas['MemoryChunk'] => ({
  id: ids.chunk(n),
  scope: 'general',
  projectId: null,
  importance: 3,
  pinned: false,
  sourceRef: null,
  createdBy: 'agent',
  createdAt: at(-n * 1440),
  updatedAt: at(-n * 720),
  deletedAt: null,
  lastRetrievedAt: at(-n * 60),
  ...overrides,
})

const chunks: Schemas['MemoryChunk'][] = [
  chunk(1, { type: 'preference', title: 'Prefers PETG for outdoor parts', content: 'Lukas prefers PETG for outdoor parts and keeps two spools in the dry box.', importance: 5, pinned: true, createdBy: 'user', sourceRef: 'chat:2026-09-12' }),
  chunk(2, { type: 'device', title: 'Voron 2.4 350', content: 'The Voron 2.4 (350 mm) runs Klipper with a Stealthburner toolhead and a Revo hotend.', importance: 4, scope: 'project', projectId: ids.voron }),
  chunk(3, { type: 'procedure', title: 'Bed leveling routine', content: 'Run QUAD_GANTRY_LEVEL, then BED_MESH_CALIBRATE with the bed at 85 °C for PETG.', scope: 'project', projectId: ids.voron }),
  chunk(4, { type: 'person', title: 'Anna', content: 'Anna is Lukas’s sister; she borrows the resin printer now and then.', importance: 2 }),
  chunk(5, { type: 'fact', title: 'Kids room lights at bedtime', content: 'The kids room lights dim to 20 % at 19:30 on school nights.', importance: 3 }),
  chunk(6, { type: 'event', title: 'Revo nozzle swap', content: 'Swapped the Revo nozzle to 0.6 mm on 2026-09-20 for faster prints.', scope: 'project', projectId: ids.voron }),
  chunk(7, { type: 'device', title: 'Garden valves', content: 'Four ESPHome valves water the beds; zone 3 leaks when the pressure is above 3 bar.', scope: 'project', projectId: ids.garden }),
  chunk(8, { type: 'preference', title: 'Briefing length', content: 'Keep the morning briefing under one minute; skip the weather on weekends.', importance: 4, createdBy: 'user' }),
]

const edges: Schemas['MemoryEdge'][] = [
  { from: ids.chunk(3), to: ids.chunk(2), kind: 'dependsOn', createdAt: at(-3000) },
  { from: ids.chunk(6), to: ids.chunk(2), kind: 'relatesTo', createdAt: at(-2000) },
  { from: ids.chunk(1), to: ids.chunk(3), kind: 'relatesTo', createdAt: at(-1500) },
  { from: ids.chunk(8), to: ids.chunk(5), kind: 'contradicts', createdAt: at(-900) },
]

const projects: Schemas['ProjectView'][] = [
  {
    id: ids.voron,
    name: 'Voron build',
    description: 'Voron 2.4 build, tuning and print profiles.',
    createdAt: at(-60 * 1440),
    lastActiveAt: at(-6),
    chunkCount: 12,
    aliases: [
      { id: id(11, 1), phrase: 'the Voron', status: 'active', confirmations: 9, createdAt: at(-50 * 1440) },
      { id: id(11, 2), phrase: 'the big printer', status: 'candidate', confirmations: 2, createdAt: at(-2 * 1440) },
    ],
  },
  {
    id: ids.garden,
    name: 'Garden irrigation',
    description: 'ESPHome valves, schedules and the rain sensor.',
    createdAt: at(-120 * 1440),
    lastActiveAt: at(-9 * 1440),
    chunkCount: 5,
    aliases: [{ id: id(11, 3), phrase: 'watering', status: 'active', confirmations: 4, createdAt: at(-100 * 1440) }],
  },
]

const configSummary: Schemas['ConfigSummary'] = {
  errors: [{ file: 'risk.yaml', message: 'Line 14: unknown tier "ask". The previous rules stay active.', previousVersionActive: true }],
  skills: [
    { name: 'printing', description: 'Klipper, slicing and print troubleshooting.', files: ['skills/printing/SKILL.md', 'skills/printing/macros.md'] },
    { name: 'home', description: 'Rooms, routines and who lives here.', files: ['skills/home/SKILL.md'] },
  ],
  templates: [
    { name: 'default', description: 'Voice and chat turns.', modelTier: 'standard', tools: ['ha.*', 'memory.*', 'printer.*'], skills: null, maxSteps: 8, maxAdvisorCalls: 1, builtIn: true },
    { name: 'briefing', description: 'Morning briefing task.', modelTier: 'standard', tools: ['calendar.*', 'mail.search', 'printer.status'], skills: ['home'], maxSteps: 12, maxAdvisorCalls: 0, builtIn: false },
  ],
  mcpServers: [
    { id: 'home-assistant', namespace: 'ha', transport: 'streamableHttp', target: 'http://homeassistant.local:8123/mcp_server/sse', untrusted: false, authTokenSecret: 'ha_token', environmentSecrets: [], state: 'connected', error: null, toolCount: 23, file: 'mcp/home-assistant.yaml', untrustedTools: ['todo__get_items'], warnings: [] },
    { id: 'notes-search', namespace: 'notes', transport: 'stdio', target: 'notes-mcp', untrusted: true, authTokenSecret: null, environmentSecrets: ['notes_repo_token'], state: 'down', error: 'Process exited with code 1.', toolCount: 0, file: 'mcp/notes.yaml', untrustedTools: [], warnings: [] },
  ],
  riskRules: [
    { tool: 'ha.lock_*', match: {}, args: {}, tier: 'confirm' },
    { tool: 'ha.light_turn_on', match: { area: ['kids_room'] }, args: {}, tier: 'safe' },
    { tool: 'printer.start_print', match: {}, args: {}, tier: 'confirm' },
    { tool: 'printer.gcode', match: {}, args: {}, tier: 'forbidden' },
  ],
  models: [
    { key: 'fast', tier: 'fast', provider: 'ollama', model: 'qwen3:4b', endpoint: 'http://ollama.local:11434', apiKeySecret: null, vision: false, tools: true, contextLength: 32_768 },
    { key: 'standard', tier: 'standard', provider: 'ollama', model: 'qwen3:14b', endpoint: 'http://ollama.local:11434', apiKeySecret: null, vision: true, tools: true, contextLength: 65_536 },
    { key: 'advisor', tier: 'advisor', provider: 'anthropic', model: 'claude-sonnet-5-5', endpoint: null, apiKeySecret: 'anthropic_api_key', vision: true, tools: true, contextLength: 200_000 },
  ],
  satellites: [
    { id: 'assist_satellite.kitchen', verbosity: 'short', notes: 'Shared device, no locks.', denyTools: [], denyConfirm: ['ha.lock_*'] },
    { id: 'assist_satellite.kids_room', verbosity: 'short', notes: null, denyTools: ['printer.*', 'mail.*'], denyConfirm: ['*'] },
  ],
}

const proposals: Schemas['ProposalRecord'][] = [
  {
    id: ids.proposal,
    taskId: ids.printTask,
    executorSessionId: id(12, 1),
    kind: 'notes',
    target: 'notes',
    branch: 'marshal/voron-input-shaper',
    title: 'Add input shaper results to the Voron notes',
    diff: '--- a/voron/tuning.md\n+++ b/voron/tuning.md\n@@ -12,3 +12,6 @@\n ## Input shaper\n-Not measured yet.\n+Measured 2026-10-01 with the ADXL345:\n+- X: mzv at 54.2 Hz\n+- Y: 2hump_ei at 41.8 Hz\n',
    status: 'open',
    createdAt: at(-30),
    decidedAt: null,
  },
]

const taskDetail: Schemas['TaskDetail'] = {
  task: tasks[1]!,
  notes: 'Skip the weather on weekends (memory: "Briefing length").',
  result: {
    speech: 'Good morning. 14 degrees and cloudy, two meetings, and the Voron finished overnight.',
    body: '## Today\n\n- **Weather:** 14 °C, cloudy, rain after 16:00\n- **Calendar:** 09:30 stand-up, 14:00 dentist\n- **Mail:** 3 unread, none urgent\n- **Printer:** `benchy_petg_0.2mm.gcode` finished at 04:12\n',
  },
  deliveries: [
    { channel: 'voice', target: 'assist_satellite.kitchen', priority: 'normal', followUp: false },
    { channel: 'push', target: 'notify.mobile_app_pixel_9', priority: 'normal', followUp: false, fallback: 'inbox' },
  ],
  overrideQuietHoursUntil: null,
  runs: runs.slice(2, 4),
  notifications: notifications.slice(1, 2),
  executorSessions: [],
}

const files: Schemas['PrinterFile'][] = [
  { path: 'gridfinity_bin_2x3.gcode', modified: at(-40), size: 2_481_220 },
  { path: 'benchy_petg_0.2mm.gcode', modified: at(-3 * 1440), size: 1_204_331 },
  { path: 'voron/stealthburner_main_body.gcode', modified: at(-20 * 1440), size: 8_902_114 },
]

const artifactItem = (n: number, name: string, kind: Schemas['ArtifactKind'], mediaType: string, size: number, source: Schemas['ArtifactSource'], sourceReference: string | null, tainted: boolean, minutes: number) => ({
  id: id(16, n),
  name,
  kind,
  mediaType,
  size,
  source,
  sourceReference,
  tainted,
  createdAt: at(minutes),
  url: `/api/v1/artifacts/${id(16, n)}/content`,
})

const artifacts = [
  artifactItem(1, 'printer-notes.md', 'markdown', 'text/markdown', 1_840, 'executor', `task:${ids.backupCheck}`, true, -35),
  artifactItem(2, 'voron_2026-10-08.jpg', 'image', 'image/jpeg', 182_330, 'printer', 'printer:voron', false, -12),
  artifactItem(3, 'filament_usage.py', 'code', 'text/x-python', 2_410, 'executor', `task:${ids.backupCheck}`, true, -90),
  artifactItem(4, 'invoice-filament.pdf', 'pdf', 'application/pdf', 96_120, 'mail', 'mail:private', true, -26 * 60),
  artifactItem(5, 'spool_holder.stl', 'model3d', 'model/stl', 412_884, 'upload', null, false, -3 * 1440),
]

export const normal = {
  '/api/v1/me': me,
  '/api/v1/me/preferences': preferences,
  '/api/v1/auth/antiforgery': { token: 'preview-token' },
  '/api/v1/auth/providers': [
    { name: 'local', type: 'local', displayName: 'Password or passkey' },
    { name: 'authentik', type: 'oidc', displayName: 'Authentik' },
  ],
  '/api/v1/auth/local/setup': { available: false },
  '/api/v1/approvals': approvals,
  '/api/v1/proposals': proposals,
  '/api/v1/proposals/{id}': proposals[0]!,
  '/api/v1/notifications': notifications,
  '/api/v1/tasks': tasks,
  '/api/v1/tasks/{id}': taskDetail,
  '/api/v1/tasks/kill-switch': { active: false, cancelled: null },
  '/api/v1/conversations': conversations,
  '/api/v1/conversations/{id}': conversationDetails[id(10, 2)]!,
  '/api/v1/runs': runs,
  '/api/v1/runs/{id}': runDetail,
  '/api/v1/cost': cost,
  '/api/v1/system': {
    version: '0.1.0',
    supportedApiVersions: ['1.0'],
    deprecatedApiVersions: [],
    eventSources: [
      { source: 'ha', connected: true, webhook: false, lastReceivedAt: null },
      { source: 'mail', connected: true, webhook: false, lastReceivedAt: null },
      { source: 'printer', connected: true, webhook: false, lastReceivedAt: null },
      { source: 'uptime-kuma', connected: true, webhook: true, lastReceivedAt: at(-18) },
      { source: 'proxmox', connected: true, webhook: true, lastReceivedAt: at(-600) },
    ],
    simulatedIntegrations: [],
  },
  '/api/v1/config': configSummary,
  '/api/v1/printers': [
    {
      name: 'voron',
      online: true,
      state: printerState,
      macros: ['PAUSE', 'RESUME', 'CANCEL_PRINT', 'LOAD_FILAMENT', 'UNLOAD_FILAMENT'],
      webUrl: 'http://voron.local',
      babysitter: {
        active: true,
        verdicts: [
          { at: at(-2), outcome: 'ok', reason: 'Layers look clean.', snapshotUrl: null },
          { at: at(-12), outcome: 'warning', reason: 'Possible layer shift at layer 112.', snapshotUrl: null },
        ],
        pausedAt: null,
      },
    },
  ],
  '/api/v1/printers/{printer}': printerState,
  '/api/v1/printers/{printer}/files': files,
  '/api/v1/printers/{printer}/queue': { state: 'ready', jobs: [{ jobId: 'q-1', filename: 'gridfinity_bin_2x3.gcode', addedAt: at(-30) }] },
  '/api/v1/printers/{printer}/history': [
    { jobId: 'j-41', filename: 'benchy_petg_0.2mm.gcode', status: 'in_progress', startedAt: at(-52), endedAt: null, printDuration: 3120, totalDuration: 3300, filamentUsed: 4210, exists: true },
    { jobId: 'j-40', filename: 'stealthburner_main_body.gcode', status: 'completed', startedAt: at(-1500), endedAt: at(-1100), printDuration: 23_400, totalDuration: 24_000, filamentUsed: 28_400, exists: true },
    { jobId: 'j-39', filename: 'cable_chain_link.gcode', status: 'cancelled', startedAt: at(-3000), endedAt: at(-2980), printDuration: 1_100, totalDuration: 1_200, filamentUsed: 800, exists: false },
  ],
  '/api/v1/mail/accounts': [
    { id: id(13, 1), alias: 'private', provider: 'imap', strategy: 'password', host: 'imap.example.com', port: 993, security: 'tls', userName: 'lukas@example.com', draftsFolder: 'Drafts', createdAt: at(-80 * 1440) },
    { id: id(13, 2), alias: 'work', provider: 'imap', strategy: 'oauth_google', host: 'imap.gmail.com', port: 993, security: 'tls', userName: 'lukas@work.example', draftsFolder: '[Gmail]/Drafts', createdAt: at(-20 * 1440) },
  ],
  '/api/v1/mail/oauth/providers': [
    { name: 'google', configured: true },
    { name: 'microsoft', configured: false },
  ],
  '/api/v1/me/tokens': [
    { id: id(14, 1), name: 'Home Assistant', scopes: ['conversation:write'], kind: 'standard', createdAt: at(-90 * 1440), expiresAt: null, revokedAt: null, readOnly: false },
    { id: id(14, 2), name: 'Backup script', scopes: ['memory:read'], kind: 'standard', createdAt: at(-40 * 1440), expiresAt: at(50 * 1440), revokedAt: null, readOnly: false },
    { id: id(14, 3), name: 'Old laptop', scopes: ['conversation:write', 'tasks:read'], kind: 'standard', createdAt: at(-200 * 1440), expiresAt: null, revokedAt: at(-30 * 1440), readOnly: false },
  ],
  '/api/v1/me/secrets': {
    secrets: [
      { name: 'freshrss_api', scope: 'user', origin: 'database', updatedAt: at(-12 * 1440), readOnly: false },
      { name: 'moonraker_voron', scope: 'instance', origin: 'environment', updatedAt: null, readOnly: true },
      { name: 'anthropic_api_key', scope: 'instance', origin: 'environment', updatedAt: null, readOnly: true },
    ],
    unresolved: [{ name: 'notes_repo_token', referencedBy: ['mcp/notes.yaml'] }],
  },
  '/api/v1/secrets/instance': [
    { name: 'ha_token', scope: 'instance', origin: 'database', updatedAt: at(-60 * 1440), readOnly: false },
    { name: 'moonraker_voron', scope: 'instance', origin: 'environment', updatedAt: null, readOnly: true },
  ],
  '/api/v1/artifacts': { items: artifacts, usedBytes: 695_584, quotaBytes: 5 * 1024 * 1024 * 1024 },
  '/api/v1/artifacts/{id}': artifacts[0]!,
  '/api/v1/memory/chunks': chunks,
  '/api/v1/memory/chunks/{id}': {
    chunk: chunks[2]!,
    versions: [{ chunkId: ids.chunk(3), content: 'Run QUAD_GANTRY_LEVEL, then BED_MESH_CALIBRATE.', importance: 3, sourceRef: null, replacedAt: at(-1500) }],
    edges: edges.filter((edge) => edge.from === ids.chunk(3) || edge.to === ids.chunk(3)),
  },
  '/api/v1/memory/chunks/{id}/history': [
    { chunkId: ids.chunk(9), kind: 'predecessor', title: 'Bed leveling', content: 'Level the bed with the paper method.', importance: 2, validFrom: at(-90 * 1440) },
    { chunkId: ids.chunk(3), kind: 'version', title: 'Bed leveling routine', content: 'Run QUAD_GANTRY_LEVEL, then BED_MESH_CALIBRATE.', importance: 3, validFrom: at(-3 * 1440) },
    { chunkId: ids.chunk(3), kind: 'current', title: 'Bed leveling routine', content: chunks[2]!.content, importance: 3, validFrom: at(-1500) },
  ],
  '/api/v1/memory/audit': {
    stale: [chunks[3]!],
    contradictions: [{ from: chunks[7]!, to: chunks[4]!, since: at(-900) }],
    orphans: [chunks[4]!],
    danglingEdges: [],
  },
  '/api/v1/memory/graph': {
    nodes: chunks.map((item) => ({ id: item.id, title: item.title, type: item.type, projectId: item.projectId, pinned: item.pinned, importance: item.importance })),
    edges,
  },
  '/api/v1/memory/scatter': chunks.map((item, index) => ({
    id: item.id,
    title: item.title,
    type: item.type,
    projectId: item.projectId,
    x: [0.42, 0.61, 0.58, -0.51, -0.22, 0.66, -0.12, -0.31][index]!,
    y: [0.18, 0.44, 0.31, -0.42, -0.08, 0.52, 0.71, -0.16][index]!,
  })),
  '/api/v1/memory/projects': projects,
  '/api/v1/memory/projects/{id}/feedback': [
    { id: id(15, 1), kind: 'confirmed', phrase: 'the Voron', runId: ids.run(3), createdAt: at(-24) },
    { id: id(15, 2), kind: 'switched', phrase: 'the big printer', runId: ids.run(2), createdAt: at(-2 * 1440) },
    { id: id(15, 3), kind: 'rejected', phrase: 'garden printer', runId: null, createdAt: at(-5 * 1440) },
  ],
} satisfies Fixtures

/** The body of a GET path in the normal scenario. */
export function sample<P extends keyof typeof normal>(path: P): GetData<P> {
  return structuredClone(normal[path]) as GetData<P>
}
