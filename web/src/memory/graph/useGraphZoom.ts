import { select } from 'd3-selection'
import 'd3-transition'
import { zoom, zoomIdentity, type D3ZoomEvent } from 'd3-zoom'
import { shallowRef, watch, type Ref, type ShallowRef } from 'vue'

export interface Bounds {
  minX: number
  minY: number
  maxX: number
  maxY: number
}

/** Room along the view's edges kept free of the graph (for overlays such as the legend and the zoom buttons). */
export interface Insets {
  top: number
  right: number
  bottom: number
  left: number
}

const noInsets: Insets = { top: 0, right: 0, bottom: 0, left: 0 }

export interface ZoomTransform {
  k: number
  x: number
  y: number
}

/** Room around the nodes for their labels (half a long label across, a line above). */
export const labelRoom = { x: 60, y: 40 }
export const scaleExtent: [number, number] = [0.25, 4]
const animation = 250

/** The bounds of the nodes' circles plus label room. */
export function graphBounds(nodes: readonly { x: number; y: number; r: number }[]): Bounds {
  let minX = Infinity
  let minY = Infinity
  let maxX = -Infinity
  let maxY = -Infinity
  for (const { x, y, r } of nodes) {
    minX = Math.min(minX, x - r)
    maxX = Math.max(maxX, x + r)
    minY = Math.min(minY, y - r)
    maxY = Math.max(maxY, y + r)
  }
  if (!Number.isFinite(minX)) return { minX: -labelRoom.x, maxX: labelRoom.x, minY: -labelRoom.y, maxY: labelRoom.y }
  return { minX: minX - labelRoom.x, maxX: maxX + labelRoom.x, minY: minY - labelRoom.y, maxY: maxY + labelRoom.y }
}

/**
 * The transform that shows `bounds` whole and centred in a `width` × `height` view (less `insets`), at one scale for
 * both axes.
 */
export function fitTransform(bounds: Bounds, width: number, height: number, insets: Insets = noInsets): ZoomTransform {
  const spanX = Math.max(bounds.maxX - bounds.minX, 1)
  const spanY = Math.max(bounds.maxY - bounds.minY, 1)
  const innerWidth = Math.max(width - insets.left - insets.right, 1)
  const innerHeight = Math.max(height - insets.top - insets.bottom, 1)
  const k = Math.min(scaleExtent[1], Math.max(scaleExtent[0], Math.min(innerWidth / spanX, innerHeight / spanY)))
  return {
    k,
    x: insets.left + innerWidth / 2 - ((bounds.minX + bounds.maxX) / 2) * k,
    y: insets.top + innerHeight / 2 - ((bounds.minY + bounds.maxY) / 2) * k,
  }
}

export interface GraphZoom {
  transform: Readonly<ShallowRef<ZoomTransform>>
  /** The scale of the last fit: the base of the label tiers. */
  fitScale: Readonly<ShallowRef<number>>
  fitTo(bounds: Bounds, animate?: boolean, insets?: Insets): void
  zoomBy(factor: number): void
}

/**
 * d3-zoom on the `<svg>`: wheel and background drag (not on nodes, which have their own drag), plus `fitTo` and
 * `zoomBy` for the on-screen controls. Vue draws from `transform`; d3 never touches the drawing.
 */
export function useGraphZoom(svg: Readonly<Ref<SVGSVGElement | null>>, size: Readonly<Ref<{ width: number; height: number }>>, reducedMotion: Readonly<Ref<boolean>>): GraphZoom {
  const transform = shallowRef<ZoomTransform>({ k: 1, x: 0, y: 0 })
  const fitScale = shallowRef(1)
  const behavior = zoom<SVGSVGElement, unknown>()
    .scaleExtent(scaleExtent)
    .extent(() => [
      [0, 0],
      [size.value.width, size.value.height],
    ])
    .filter((event: Event & { button?: number; ctrlKey?: boolean }) => {
      if (event.type !== 'wheel' && event.ctrlKey) return false
      if (event.button) return false
      return !(event.target instanceof Element && event.target.closest('[data-node], [data-controls]'))
    })
    .on('zoom', (event: D3ZoomEvent<SVGSVGElement, unknown>) => {
      transform.value = { k: event.transform.k, x: event.transform.x, y: event.transform.y }
    })

  watch(
    svg,
    (element, _, onCleanup) => {
      if (!element) return
      const selection = select(element).call(behavior)
      onCleanup(() => selection.on('.zoom', null))
    },
    { immediate: true },
  )

  const animated = (animate: boolean) => animate && !reducedMotion.value

  function fitTo(bounds: Bounds, animate = true, insets: Insets = noInsets) {
    const target = fitTransform(bounds, size.value.width, size.value.height, insets)
    fitScale.value = target.k
    const element = svg.value
    if (!element) {
      transform.value = target
      return
    }
    const next = zoomIdentity.translate(target.x, target.y).scale(target.k)
    if (animated(animate)) select(element).transition().duration(animation).call(behavior.transform, next)
    else select(element).call(behavior.transform, next)
  }

  function zoomBy(factor: number) {
    const element = svg.value
    if (!element) return
    if (animated(true)) select(element).transition().duration(animation).call(behavior.scaleBy, factor)
    else select(element).call(behavior.scaleBy, factor)
  }

  return { transform, fitScale, fitTo, zoomBy }
}
