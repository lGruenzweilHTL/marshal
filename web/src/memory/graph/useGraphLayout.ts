import {
  forceCenter,
  forceCollide,
  forceLink,
  forceManyBody,
  forceSimulation,
  forceX,
  forceY,
  type Simulation,
  type SimulationLinkDatum,
  type SimulationNodeDatum,
} from 'd3-force'
import { onScopeDispose, shallowRef, watch, type Ref, type ShallowRef } from 'vue'
import { adjacency, degrees, knownEdges, nodeRadius, type GraphEdge, type GraphNode } from './graphModel'

export interface LayoutNode extends SimulationNodeDatum {
  id: string
  node: GraphNode
  r: number
  degree: number
  x: number
  y: number
}

export interface LayoutLink extends SimulationLinkDatum<LayoutNode> {
  edge: GraphEdge
  source: LayoutNode
  target: LayoutNode
}

export interface GraphLayoutOptions {
  /** Pull each project's chunks towards its own anchor (the memory map). */
  anchors?: boolean
  /** A node fixed at the origin (the local graph's chunk). */
  centreId?: string
  /** Multiplies the link length: a small graph has room for longer links, so its labels do not collide. */
  spread?: number
}

/** Where warm-up stops ticking off screen and the visible settle starts. */
const warmAlpha = 0.05
/** Length of the visible settle, in frames (about 600 ms). */
const settleFrames = 36
/** A settle frame slower than this (about 20 fps) means the device cannot animate this graph smoothly. */
const slowFrame = 50

/**
 * Each project's anchor on a ring around the origin, ordered by project id, starting on the left; the ring grows with
 * the project count and is wider than tall, like the drawing.
 */
function projectAnchors(nodes: readonly GraphNode[]): Map<string, { x: number; y: number }> {
  const projects = [...new Set(nodes.flatMap((node) => (node.projectId ? [node.projectId] : [])))].sort()
  const radius = 150 * Math.sqrt(projects.length)
  return new Map(
    projects.map((id, index) => {
      const angle = Math.PI + (2 * Math.PI * index) / projects.length
      return [id, { x: 1.3 * radius * Math.cos(angle), y: radius * Math.sin(angle) }]
    }),
  )
}

/**
 * A stopped simulation of the graph: many-body repulsion (Barnes–Hut), links, collision, a weak pull to each node's
 * project anchor (or the origin), and centring. d3 seeds its own random source and places nodes on a fixed spiral, so
 * the same input gives the same layout.
 */
export function createSimulation(graphNodes: readonly GraphNode[], graphEdges: readonly GraphEdge[], options: GraphLayoutOptions = {}) {
  const edges = knownEdges(graphNodes, graphEdges)
  const degree = degrees(adjacency(graphNodes, edges))
  const nodes: LayoutNode[] = graphNodes.map((node) => {
    const linkCount = degree.get(node.id) ?? 0
    const fixed = node.id === options.centreId
    // NaN tells d3 to place the node on its spiral (or at fx/fy when fixed).
    return { id: node.id, node, degree: linkCount, r: nodeRadius(node.importance, linkCount), x: NaN, y: NaN, ...(fixed ? { fx: 0, fy: 0 } : {}) }
  })
  const byId = new Map(nodes.map((node) => [node.id, node]))
  const links: LayoutLink[] = edges.map((edge) => ({ edge, source: byId.get(edge.from)!, target: byId.get(edge.to)! }))
  const anchors = options.anchors ? projectAnchors(graphNodes) : new Map<string, { x: number; y: number }>()
  const anchor = (node: LayoutNode) => (node.node.projectId && anchors.get(node.node.projectId)) || { x: 0, y: 0 }

  const simulation = forceSimulation<LayoutNode>(nodes)
    .stop()
    .force('charge', forceManyBody<LayoutNode>().strength(-160).distanceMax(320))
    .force('link', forceLink<LayoutNode, LayoutLink>(links).distance((link) => (link.edge.kind === 'dependsOn' ? 40 : 52) * (options.spread ?? 1)))
    .force('collide', forceCollide<LayoutNode>((node) => node.r + 8))
    .force('x', forceX<LayoutNode>((node) => anchor(node).x).strength(0.08))
    .force('y', forceY<LayoutNode>((node) => anchor(node).y).strength(0.08))
  // Centring shifts every node; with a fixed centre node the pull to the origin is enough.
  if (options.centreId === undefined) simulation.force('center', forceCenter(0, 0))
  return { simulation, nodes, links }
}

/** Ticks until alpha (cooling towards zero) falls below `alpha`. */
function ticksUntil(simulation: Simulation<LayoutNode, LayoutLink>, alpha: number): number {
  const current = simulation.alpha()
  if (current <= alpha) return 0
  return Math.ceil(Math.log(alpha / current) / Math.log(1 - simulation.alphaDecay()))
}

/** The final layout, ticked headless: node id to position. */
export function layoutGraph(nodes: readonly GraphNode[], edges: readonly GraphEdge[], options: GraphLayoutOptions = {}): Map<string, { x: number; y: number }> {
  const { simulation, nodes: laid } = createSimulation(nodes, edges, options)
  simulation.tick(ticksUntil(simulation, simulation.alphaMin()))
  return new Map(laid.map((node) => [node.id, { x: node.x, y: node.y }]))
}

export interface GraphLayout {
  /** Nodes with their current positions; mutated in place by the simulation, re-rendered through `frame`. */
  nodes: Readonly<ShallowRef<LayoutNode[]>>
  links: Readonly<ShallowRef<LayoutLink[]>>
  /** Counts rendered simulation steps; read it to re-render positions. */
  frame: Readonly<Ref<number>>
  /** True while the layout moves (settle or drag). */
  moving: Readonly<Ref<boolean>>
  dragStart(id: string): void
  dragMove(id: string, x: number, y: number): void
  dragEnd(id: string): void
}

/**
 * Runs the layout for the graph `input` returns: warms up off screen, then lets the rest settle over about 600 ms, or
 * jumps to the end under reduced motion. Dragging a node pins it, reheats the layout and releases it on drop.
 */
export function useGraphLayout(
  input: () => { nodes: readonly GraphNode[]; edges: readonly GraphEdge[] } | null,
  options: GraphLayoutOptions,
  reducedMotion: Readonly<Ref<boolean>>,
): GraphLayout {
  const nodes = shallowRef<LayoutNode[]>([])
  const links = shallowRef<LayoutLink[]>([])
  const frame = shallowRef(0)
  const moving = shallowRef(false)
  let simulation: Simulation<LayoutNode, LayoutLink> | null = null
  let request = 0
  let perFrame = 1
  let dragging = false
  const animated = () => !reducedMotion.value && typeof requestAnimationFrame === 'function'

  function cancel() {
    if (request) cancelAnimationFrame(request)
    request = 0
  }

  function finish() {
    if (!simulation) return
    simulation.tick(ticksUntil(simulation, simulation.alphaMin()))
    moving.value = false
    frame.value++
  }

  let lastFrame = 0
  let slowFrames = 0

  function step(time: number) {
    request = 0
    if (!simulation) return
    // Two slow settle frames in a row: jump to the final layout instead of stuttering towards it. Drags keep going.
    slowFrames = !dragging && lastFrame && time - lastFrame > slowFrame ? slowFrames + 1 : 0
    lastFrame = time
    if (slowFrames >= 2) return finish()
    simulation.tick(dragging ? 1 : perFrame)
    frame.value++
    if (dragging || simulation.alpha() >= simulation.alphaMin()) request = requestAnimationFrame(step)
    else moving.value = false
  }

  function run(ticksPerFrame: number) {
    if (!animated() && !dragging) return finish()
    perFrame = ticksPerFrame
    moving.value = true
    lastFrame = 0
    slowFrames = 0
    if (!request) request = requestAnimationFrame(step)
  }

  watch(
    input,
    (graph) => {
      cancel()
      dragging = false
      simulation = null
      if (!graph) {
        nodes.value = []
        links.value = []
        return
      }
      const created = createSimulation(graph.nodes, graph.edges, options)
      simulation = created.simulation
      nodes.value = created.nodes
      links.value = created.links
      simulation.tick(ticksUntil(simulation, warmAlpha))
      frame.value++
      run(Math.max(1, Math.ceil(ticksUntil(simulation, simulation.alphaMin()) / settleFrames)))
    },
    { immediate: true },
  )

  const find = (id: string) => nodes.value.find((node) => node.id === id)

  function dragStart(id: string) {
    const node = find(id)
    if (!simulation || !node) return
    node.fx = node.x
    node.fy = node.y
    dragging = true
    simulation.alphaTarget(0.3).alpha(Math.max(simulation.alpha(), 0.3))
    if (typeof requestAnimationFrame === 'function') run(1)
  }

  function dragMove(id: string, x: number, y: number) {
    const node = find(id)
    if (!node || id === options.centreId) return
    node.fx = x
    node.fy = y
    if (typeof requestAnimationFrame !== 'function' && simulation) {
      simulation.tick()
      frame.value++
    }
  }

  function dragEnd(id: string) {
    const node = find(id)
    if (!simulation || !node) return
    if (id !== options.centreId) {
      node.fx = null
      node.fy = null
    }
    dragging = false
    simulation.alphaTarget(0)
    run(2)
  }

  onScopeDispose(cancel)
  return { nodes, links, frame, moving, dragStart, dragMove, dragEnd }
}
