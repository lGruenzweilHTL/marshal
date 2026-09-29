import type { Schemas } from '../api/client'

export const projectId = '0199bbbb-0000-7000-8000-000000000001'

export function chunk(overrides: Partial<Schemas['MemoryChunk']> = {}): Schemas['MemoryChunk'] {
  return {
    id: 'AAAAAAAAAAAAAAAAAAAAAAAAAA',
    scope: 'general',
    projectId: null,
    type: 'device',
    title: 'Voron nozzle',
    content: '0.6 mm nozzle.',
    importance: 3,
    pinned: false,
    sourceRef: null,
    createdBy: 'user',
    createdAt: '2026-09-20T10:00:00Z',
    updatedAt: '2026-09-21T10:00:00Z',
    deletedAt: null,
    lastRetrievedAt: null,
    ...overrides,
  }
}

export function project(overrides: Partial<Schemas['ProjectView']> = {}): Schemas['ProjectView'] {
  return {
    id: projectId,
    name: 'Voron',
    description: 'The 3D printer.',
    createdAt: '2026-09-01T10:00:00Z',
    lastActiveAt: null,
    chunkCount: 2,
    aliases: [],
    ...overrides,
  }
}
