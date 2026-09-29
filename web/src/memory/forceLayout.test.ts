import { describe, expect, it } from 'vitest'
import { forceLayout } from './forceLayout'

function distance(a: { x: number; y: number }, b: { x: number; y: number }) {
  return Math.hypot(a.x - b.x, a.y - b.y)
}

describe('forceLayout', () => {
  it('pulls linked nodes together and pushes unlinked ones apart', () => {
    const positions = forceLayout(['a', 'b', 'c', 'd'], [
      ['a', 'b'],
      ['c', 'd'],
    ])

    expect(distance(positions.get('a')!, positions.get('b')!)).toBeLessThan(distance(positions.get('a')!, positions.get('c')!))
    expect(distance(positions.get('c')!, positions.get('d')!)).toBeLessThan(distance(positions.get('b')!, positions.get('d')!))
  })

  it('is deterministic and finite', () => {
    const nodes = ['a', 'b', 'c', 'd', 'e']
    const edges: [string, string][] = [
      ['a', 'b'],
      ['b', 'c'],
    ]

    const first = forceLayout(nodes, edges)
    const second = forceLayout(nodes, edges)

    expect([...first.entries()]).toEqual([...second.entries()])
    for (const point of first.values()) expect(Number.isFinite(point.x) && Number.isFinite(point.y)).toBe(true)
  })

  it('ignores edges to unknown nodes', () => {
    expect(forceLayout(['a'], [['a', 'missing']]).size).toBe(1)
  })
})
