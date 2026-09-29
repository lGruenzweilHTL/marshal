import type { Schemas } from '../api/client'

export const chunkTypes: Schemas['ChunkType'][] = ['fact', 'preference', 'event', 'procedure', 'person', 'device']

export const historyLabels: Record<Schemas['MemoryHistoryKind'], string> = {
  predecessor: 'Superseded',
  version: 'Earlier version',
  current: 'Current',
}

/** Alias status with its confirmation count for candidates. */
export function aliasStatus(alias: Schemas['ProjectAliasView']): string {
  return alias.status === 'candidate' ? `candidate (${alias.confirmations} confirmation${alias.confirmations === 1 ? '' : 's'})` : alias.status
}
