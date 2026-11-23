import { describe, expect, it } from 'vitest'
import type { GraphEdge, GraphNode } from './graphModel'
import { layoutGraph } from './useGraphLayout'

function node(id: string, projectId: string | null = null): GraphNode {
  return { id, title: id, type: 'fact', projectId, pinned: false, importance: 2 }
}

function edge(from: string, to: string, kind: GraphEdge['kind'] = 'relatesTo'): GraphEdge {
  return { from, to, kind, createdAt: '2026-09-21T10:00:00Z' }
}

function distance(a: { x: number; y: number }, b: { x: number; y: number }) {
  return Math.hypot(a.x - b.x, a.y - b.y)
}

describe('layoutGraph', () => {
  const nodes = ['a', 'b', 'c', 'd', 'e', 'f'].map((id) => node(id))
  const edges = [edge('a', 'b'), edge('b', 'c', 'dependsOn'), edge('d', 'e')]

  it('gives the same layout for the same input', () => {
    const first = layoutGraph(nodes, edges)
    const second = layoutGraph(nodes, edges)

    expect([...first.entries()]).toEqual([...second.entries()])
    for (const point of first.values()) expect(Number.isFinite(point.x) && Number.isFinite(point.y)).toBe(true)
  })

  it('places linked nodes closer together than unlinked ones', () => {
    const positions = layoutGraph(nodes, edges)
    const at = (id: string) => positions.get(id)!

    expect(distance(at('a'), at('b'))).toBeLessThan(distance(at('a'), at('d')))
    expect(distance(at('d'), at('e'))).toBeLessThan(distance(at('e'), at('b')))
  })

  it('pulls the nodes of one project together, away from other projects', () => {
    const grouped = [
      ...['p1', 'p2', 'p3', 'p4'].map((id) => node(id, 'project-a')),
      ...['q1', 'q2', 'q3', 'q4'].map((id) => node(id, 'project-b')),
      node('loose'),
    ]
    const positions = layoutGraph(grouped, [], { anchors: true })
    const at = (id: string) => positions.get(id)!
    const within = Math.max(distance(at('p1'), at('p4')), distance(at('q1'), at('q4')), distance(at('p2'), at('p3')))
    const across = Math.min(...['p1', 'p2', 'p3', 'p4'].flatMap((p) => ['q1', 'q2', 'q3', 'q4'].map((q) => distance(at(p), at(q)))))

    expect(within).toBeLessThan(across)
  })

  it('keeps a fixed centre node at the origin', () => {
    const positions = layoutGraph(nodes, edges, { centreId: 'b' })

    expect(positions.get('b')).toEqual({ x: 0, y: 0 })
  })

  it('ignores edges to unknown nodes', () => {
    const positions = layoutGraph([node('a')], [edge('a', 'missing')])

    expect([...positions.keys()]).toEqual(['a'])
  })
})
