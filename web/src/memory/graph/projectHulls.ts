export interface HullNode {
  x: number
  y: number
  r: number
  projectId: string | null
}

export interface ProjectHull {
  projectId: string
  /** One circle sub-path per node; filled with the non-zero rule they draw as their union. */
  path: string
  /** Where the project name goes: the top centre of the backdrop's bounding box. */
  label: { x: number; y: number }
}

/** How far a backdrop reaches past a node's circle. */
export const backdropPadding = 24

function circle(x: number, y: number, r: number): string {
  return `M${x - r} ${y}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0Z`
}

/**
 * A backdrop per project: the union of padded circles around its nodes. Unlike a convex hull, it hugs concave clusters
 * and does not swallow other projects' nodes. Chunks without a project get none.
 */
export function projectHulls(nodes: readonly HullNode[], padding = backdropPadding): ProjectHull[] {
  const groups = new Map<string, HullNode[]>()
  for (const node of nodes) {
    if (node.projectId === null) continue
    const group = groups.get(node.projectId)
    if (group) group.push(node)
    else groups.set(node.projectId, [node])
  }
  return [...groups]
    .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
    .map(([projectId, members]) => {
      let left = Infinity
      let right = -Infinity
      let top = Infinity
      for (const { x, y, r } of members) {
        left = Math.min(left, x - r - padding)
        right = Math.max(right, x + r + padding)
        top = Math.min(top, y - r - padding)
      }
      return {
        projectId,
        path: members.map(({ x, y, r }) => circle(x, y, r + padding)).join(''),
        label: { x: (left + right) / 2, y: top },
      }
    })
}
