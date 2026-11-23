import { describe, expect, it } from 'vitest'
import { fitTransform, graphBounds, labelRoom, scaleExtent } from './useGraphZoom'

describe('graphBounds', () => {
  it('covers the shown nodes plus room for their labels', () => {
    const bounds = graphBounds([
      { x: -50, y: 0, r: 5 },
      { x: 100, y: 40, r: 10 },
    ])

    expect(bounds).toEqual({ minX: -55 - labelRoom.x, maxX: 110 + labelRoom.x, minY: -5 - labelRoom.y, maxY: 50 + labelRoom.y })
  })
})

describe('fitTransform', () => {
  it('fits the bounds into the view, keeping the aspect ratio and centring them', () => {
    const transform = fitTransform({ minX: -200, maxX: 200, minY: -25, maxY: 25 }, 1000, 600)

    // Width limits: 400 graph units across 1000 px; the height has room to spare.
    expect(transform.k).toBeCloseTo(2.5)
    expect(transform.x).toBeCloseTo(500)
    expect(transform.y).toBeCloseTo(300)
  })

  it('uses one scale for both axes, set by the tighter one', () => {
    const transform = fitTransform({ minX: 0, maxX: 1000, minY: 0, maxY: 1200 }, 1000, 600)

    expect(transform.k).toBeCloseTo(0.5)
    // Centred: the middle of the bounds (500, 600) lands in the middle of the view.
    expect(transform.x + 500 * transform.k).toBeCloseTo(500)
    expect(transform.y + 600 * transform.k).toBeCloseTo(300)
  })

  it('keeps the insets free', () => {
    const transform = fitTransform({ minX: 0, maxX: 400, minY: 0, maxY: 100 }, 1000, 600, { top: 0, right: 100, bottom: 0, left: 100 })

    expect(transform.k).toBeCloseTo(2)
    expect(transform.x).toBeCloseTo(100)
  })

  it('clamps the scale to the zoom extent', () => {
    expect(fitTransform({ minX: 0, maxX: 10, minY: 0, maxY: 10 }, 1000, 600).k).toBe(scaleExtent[1])
    expect(fitTransform({ minX: 0, maxX: 100000, minY: 0, maxY: 100000 }, 1000, 600).k).toBe(scaleExtent[0])
  })
})
