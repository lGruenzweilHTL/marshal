import type { Schemas } from '../api/client'
import type { Tone } from '../ui'

export const expectationTones: Record<Schemas['EvalExpectation'], Tone> = { good: 'success', bad: 'danger', unrated: 'neutral' }
export const outcomeTones: Record<Schemas['EvalOutcome'], Tone> = { pass: 'success', changed: 'warning', diverged: 'info', failed: 'danger' }
export const statusTones: Record<Schemas['EvalRunStatus'], Tone> = { queued: 'neutral', running: 'info', completed: 'success', cancelled: 'neutral', failed: 'danger' }
