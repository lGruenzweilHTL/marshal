export interface Point {
  x: number
  y: number
}

/**
 * A small deterministic force-directed layout: all nodes repel, linked nodes attract, everything drifts to the
 * centre. Nodes start on a spiral so the same graph always gives the same picture.
 */
export function forceLayout(nodes: string[], edges: [string, string][], iterations = 300): Map<string, Point> {
  const positions = new Map<string, Point>()
  nodes.forEach((id, index) => {
    const angle = index * 2.399963
    const radius = 10 * Math.sqrt(index + 1)
    positions.set(id, { x: radius * Math.cos(angle), y: radius * Math.sin(angle) })
  })
  const links = edges.filter(([from, to]) => from !== to && positions.has(from) && positions.has(to))
  const ideal = 30

  for (let step = 0; step < iterations; step++) {
    const cooling = 1 - step / iterations
    const forces = new Map<string, Point>(nodes.map((id) => [id, { x: 0, y: 0 }]))
    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const a = positions.get(nodes[i]!)!
        const b = positions.get(nodes[j]!)!
        const dx = a.x - b.x
        const dy = a.y - b.y
        const squared = Math.max(dx * dx + dy * dy, 0.01)
        const push = (ideal * ideal) / squared
        forces.get(nodes[i]!)!.x += dx * push
        forces.get(nodes[i]!)!.y += dy * push
        forces.get(nodes[j]!)!.x -= dx * push
        forces.get(nodes[j]!)!.y -= dy * push
      }
    }

    for (const [from, to] of links) {
      const a = positions.get(from)!
      const b = positions.get(to)!
      const dx = b.x - a.x
      const dy = b.y - a.y
      const length = Math.max(Math.hypot(dx, dy), 0.01)
      const pull = (length - ideal) / length / 2
      forces.get(from)!.x += dx * pull
      forces.get(from)!.y += dy * pull
      forces.get(to)!.x -= dx * pull
      forces.get(to)!.y -= dy * pull
    }

    for (const id of nodes) {
      const point = positions.get(id)!
      const force = forces.get(id)!
      force.x -= point.x * 0.01
      force.y -= point.y * 0.01
      const size = Math.hypot(force.x, force.y)
      const limit = 5 * cooling + 0.1
      const scale = size > limit ? limit / size : 1
      point.x += force.x * scale
      point.y += force.y * scale
    }
  }

  return positions
}
