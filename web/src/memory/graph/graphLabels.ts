export interface LabelNode {
  id: string
  pinned: boolean
  importance: number
}

/** How many of the best-linked chunks are labelled at fit zoom. */
export const topByDegree = 10

/**
 * Which nodes carry a visible label at zoom `k`, given the fit zoom `k0`:
 * - at fit zoom, the pinned chunks and the ten with the most links (ties by importance, then id);
 * - from 1.5 × k0, also chunks of importance 3 or more, or with 3 or more links;
 * - from 2.5 × k0, every chunk.
 * The highlight set (the hovered or focused node and its neighbours) is always labelled.
 */
export function labelledIds(
  nodes: readonly LabelNode[],
  degree: ReadonlyMap<string, number>,
  k: number,
  k0: number,
  highlight: ReadonlySet<string>,
): Set<string> {
  const ratio = k / k0
  if (ratio >= 2.5) return new Set(nodes.map((node) => node.id))

  const linkCount = (node: LabelNode) => degree.get(node.id) ?? 0
  const ranked = [...nodes].sort((a, b) => linkCount(b) - linkCount(a) || b.importance - a.importance || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0))
  const labels = new Set(ranked.slice(0, topByDegree).map((node) => node.id))
  for (const node of nodes) {
    if (node.pinned || highlight.has(node.id)) labels.add(node.id)
    else if (ratio >= 1.5 && (node.importance >= 3 || linkCount(node) >= 3)) labels.add(node.id)
  }
  for (const id of highlight) labels.add(id)
  return labels
}
