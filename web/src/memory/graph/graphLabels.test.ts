import { describe, expect, it } from 'vitest'
import { labelledIds, type LabelNode } from './graphLabels'

const k0 = 0.8

function nodes(count: number, overrides: Record<number, Partial<LabelNode>> = {}): LabelNode[] {
  return Array.from({ length: count }, (_, index) => ({ id: `n${String(index).padStart(2, '0')}`, pinned: false, importance: 1, ...overrides[index] }))
}

function degreeMap(list: LabelNode[], values: Record<string, number> = {}): Map<string, number> {
  return new Map(list.map((node) => [node.id, values[node.id] ?? 0]))
}

describe('labelledIds', () => {
  it('labels the pinned chunks and at most the top ten by degree at fit zoom', () => {
    const list = nodes(80, { 5: { pinned: true }, 70: { pinned: true } })
    const degree = degreeMap(list, Object.fromEntries(list.map((node, index) => [node.id, index % 7])))

    const labels = labelledIds(list, degree, k0, k0, new Set())

    expect(labels.size).toBeLessThanOrEqual(12)
    expect(labels.has('n05')).toBe(true)
    expect(labels.has('n70')).toBe(true)
    // Degree 6 belongs to n06, n13, n20, … (eleven nodes); the ten with the lowest id win the tie at importance 1.
    expect([...labels].filter((id) => degree.get(id) === 6)).toHaveLength(10)
  })

  it('adds chunks of importance 3 or more, or with 3 or more links, from 1.5 times the fit zoom', () => {
    const list = nodes(30, { 20: { importance: 3 }, 21: { importance: 2 } })
    const degree = degreeMap(list, { n22: 3, n23: 2 })
    // Twelve nodes with degree 5 fill the top ten.
    for (let index = 0; index < 12; index++) degree.set(list[index]!.id, 5)

    const fit = labelledIds(list, degree, k0, k0, new Set())
    const closer = labelledIds(list, degree, k0 * 1.5, k0, new Set())

    expect(fit.has('n20')).toBe(false)
    expect(fit.has('n22')).toBe(false)
    expect(closer.has('n20')).toBe(true)
    expect(closer.has('n22')).toBe(true)
    expect(closer.has('n21')).toBe(false)
    expect(closer.has('n23')).toBe(false)
  })

  it('labels every chunk from 2.5 times the fit zoom', () => {
    const list = nodes(40)

    expect(labelledIds(list, degreeMap(list), k0 * 2.5, k0, new Set()).size).toBe(40)
  })

  it('always labels the highlight set', () => {
    const list = nodes(40)
    const degree = degreeMap(list, Object.fromEntries(list.slice(0, 20).map((node) => [node.id, 4])))

    const labels = labelledIds(list, degree, k0, k0, new Set(['n35', 'n36']))

    expect(labels.has('n35')).toBe(true)
    expect(labels.has('n36')).toBe(true)
  })

  it('breaks degree ties by importance, then by id', () => {
    const list = nodes(14, { 13: { importance: 5 } })
    const degree = degreeMap(list, Object.fromEntries(list.map((node) => [node.id, 2])))

    const labels = labelledIds(list, degree, k0, k0, new Set())

    expect([...labels].sort()).toEqual(['n00', 'n01', 'n02', 'n03', 'n04', 'n05', 'n06', 'n07', 'n08', 'n13'])
    expect(labelledIds([...list].reverse(), degree, k0, k0, new Set())).toEqual(labels)
  })
})
