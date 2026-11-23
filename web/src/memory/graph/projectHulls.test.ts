import { describe, expect, it } from 'vitest'
import { backdropPadding, projectHulls } from './projectHulls'

describe('projectHulls', () => {
  const nodes = [
    { x: 0, y: 0, r: 6, projectId: 'b' },
    { x: 40, y: 10, r: 8, projectId: 'b' },
    { x: 200, y: 200, r: 5, projectId: 'a' },
    { x: -100, y: -100, r: 5, projectId: null },
  ]

  it('draws one path per project, ordered by project id', () => {
    const hulls = projectHulls(nodes)

    expect(hulls.map((hull) => hull.projectId)).toEqual(['a', 'b'])
    // One closed circle per node of the project.
    expect(hulls[1]!.path.match(/Z/g)).toHaveLength(2)
    expect(hulls[0]!.path.match(/Z/g)).toHaveLength(1)
  })

  it('gives chunks without a project no backdrop', () => {
    expect(projectHulls([{ x: 0, y: 0, r: 5, projectId: null }])).toEqual([])
  })

  it('places the name at the top centre of the backdrop', () => {
    const hull = projectHulls(nodes).find((entry) => entry.projectId === 'b')!

    // Top: the highest padded circle edge (node 1: 0 - 6 - padding); centre between the outer left and right edges.
    expect(hull.label.y).toBe(-6 - backdropPadding)
    const left = 0 - 6 - backdropPadding
    const right = 40 + 8 + backdropPadding
    expect(hull.label.x).toBe((left + right) / 2)
  })

  it('returns nothing without nodes', () => {
    expect(projectHulls([])).toEqual([])
  })
})
