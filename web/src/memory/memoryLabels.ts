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

/** A colour per chunk type for the graph and the scatter (readable on light and dark backgrounds). */
export const typeColors: Record<Schemas['ChunkType'], string> = {
  fact: '#4e79a7',
  preference: '#f28e2b',
  event: '#e15759',
  procedure: '#76b7b2',
  person: '#59a14f',
  device: '#b07aa1',
}
