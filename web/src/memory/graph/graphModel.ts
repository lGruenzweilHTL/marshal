import type { Schemas } from '../../api/client'

export type GraphNode = Schemas['MemoryGraphNode']
export type GraphEdge = Schemas['MemoryEdge']

/** Edges whose both ends are among the nodes and that are not self-loops. */
export function knownEdges(nodes: readonly GraphNode[], edges: readonly GraphEdge[]): GraphEdge[] {
  const ids = new Set(nodes.map((node) => node.id))
  return edges.filter((edge) => edge.from !== edge.to && ids.has(edge.from) && ids.has(edge.to))
}

/** Each node's direct neighbours, in either direction. */
export function adjacency(nodes: readonly GraphNode[], edges: readonly GraphEdge[]): Map<string, Set<string>> {
  const neighbours = new Map(nodes.map((node) => [node.id, new Set<string>()]))
  for (const edge of knownEdges(nodes, edges)) {
    neighbours.get(edge.from)!.add(edge.to)
    neighbours.get(edge.to)!.add(edge.from)
  }
  return neighbours
}

/** The number of distinct neighbours per node. */
export function degrees(neighbours: ReadonlyMap<string, ReadonlySet<string>>): Map<string, number> {
  return new Map([...neighbours].map(([id, set]) => [id, set.size]))
}

/** Node radius: grows with importance and, more slowly, with the number of links. */
export function nodeRadius(importance: number, degree: number): number {
  return 4 + 1.2 * importance + 1.5 * Math.sqrt(degree)
}
