import type { Schemas } from '../api/client'
import type { Tone } from '../ui'

export type PrinterStatus = 'printing' | 'paused' | 'error' | 'idle' | 'offline' | 'complete'

/** What a printer is doing, from Moonraker's print state and Klipper's state (a Klipper shutdown is an error). */
export function printerStatus(printer: Schemas['PrinterSummary']): PrinterStatus {
  const state = printer.online ? printer.state : null
  if (!state) return 'offline'
  if (state.klippyState === 'error' || state.klippyState === 'shutdown') return 'error'
  switch (state.printState) {
    case 'printing':
    case 'paused':
    case 'error':
    case 'complete':
      return state.printState
    default:
      return 'idle'
  }
}

export const statusTone: Record<PrinterStatus, Tone> = {
  printing: 'info',
  paused: 'warning',
  error: 'danger',
  idle: 'success',
  offline: 'neutral',
  complete: 'success',
}

/** A printer that needs the user: a paused or failed print (Home shows it right after the approvals). */
export const needsAttention = (status: PrinterStatus) => status === 'paused' || status === 'error'
