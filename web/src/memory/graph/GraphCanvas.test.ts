import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { FakeServer } from '../../testing/fakeServer'
import { me, mountOptions } from '../../testing/mountApp'
import GraphCanvas from './GraphCanvas.vue'
import type { GraphEdge, GraphNode } from './graphModel'

function node(id: string, overrides: Partial<GraphNode> = {}): GraphNode {
  return { id, title: `Title ${id}`, type: 'fact', projectId: null, pinned: false, importance: 2, ...overrides }
}

function edge(from: string, to: string, kind: GraphEdge['kind'] = 'relatesTo'): GraphEdge {
  return { from, to, kind, createdAt: '2026-09-21T10:00:00Z' }
}

// A hub with eleven spokes (the hub and spokes s00–s08 fill the fit-zoom labels) and a separate pair x–y.
const spokes = Array.from({ length: 11 }, (_, index) => `s${String(index).padStart(2, '0')}`)
const nodes = [node('hub', { pinned: true, type: 'device' }), ...spokes.map((id) => node(id)), node('x', { type: 'event' }), node('y')]
const edges = [
  ...spokes.map((id) => edge(id, 'hub')),
  edge('x', 'y', 'contradicts'),
  edge('s00', 's01', 'dependsOn'),
  edge('s02', 's03', 'supersedes'),
  edge('s04', 'missing'),
]

function preferReducedMotion() {
  vi.stubGlobal('matchMedia', (query: string) => ({
    matches: query.includes('prefers-reduced-motion'),
    media: query,
    addEventListener() {},
    removeEventListener() {},
  }))
}

async function mountCanvas(props: Partial<InstanceType<typeof GraphCanvas>['$props']> = {}) {
  const { options } = await mountOptions(new FakeServer().on('GET', '/api/v1/me', 200, me), '/memory/map')
  const wrapper = mount(GraphCanvas, { ...options, props: { nodes, edges, label: 'Memory graph', ...props } })
  await flushPromises()
  return wrapper
}

// jsdom has no PointerEvent; a MouseEvent of the pointer type carries the coordinates the canvas reads.
function pointer(element: Element, type: string, clientX: number, clientY: number) {
  element.dispatchEvent(new MouseEvent(type, { bubbles: true, clientX, clientY, button: 0 }))
}

const scale = (transform: string | undefined) => Number(/scale\(([^)]+)\)/.exec(transform ?? '')![1])

describe('GraphCanvas', () => {
  beforeEach(preferReducedMotion)

  it('draws a node per chunk and an edge per known link', async () => {
    const wrapper = await mountCanvas()

    expect(wrapper.findAll('[data-node]')).toHaveLength(nodes.length)
    expect(wrapper.findAll('[data-edge]')).toHaveLength(edges.length - 1)
    expect(wrapper.get('svg').attributes('role')).toBe('img')
    expect(wrapper.get('svg').attributes('aria-label')).toBe('Memory graph')
    expect(wrapper.get('[data-node="hub"]').attributes('aria-label')).toBe('Title hub')
  })

  it('shows direction only on dependsOn and supersedes edges', async () => {
    const wrapper = await mountCanvas()

    const directed = wrapper.findAll('[data-edge]').filter((edge) => edge.find('[data-arrow]').exists())
    expect(directed.map((edge) => edge.attributes('data-edge')).sort()).toEqual(['dependsOn', 'supersedes'])
  })

  it('draws contradicts edges distinctly', async () => {
    const wrapper = await mountCanvas()

    const contradicts = wrapper.get('[data-edge="contradicts"]')
    expect(contradicts.classes()).toContain('contradicts')
    expect(wrapper.findAll('[data-edge="relatesTo"]').every((path) => !path.classes('contradicts'))).toBe(true)
  })

  it('gives pinned chunks a halo', async () => {
    const wrapper = await mountCanvas()

    expect(wrapper.get('[data-node="hub"]').find('.halo').exists()).toBe(true)
    expect(wrapper.get('[data-node="s00"]').find('.halo').exists()).toBe(false)
  })

  it('labels the pinned and best-linked chunks at fit zoom', async () => {
    const wrapper = await mountCanvas()

    expect(wrapper.find('[data-label="hub"]').exists()).toBe(true)
    expect(wrapper.find('[data-label="x"]').exists()).toBe(false)
  })

  it('highlights a hovered node and its neighbours, labels them, and dims the rest', async () => {
    const wrapper = await mountCanvas()

    await wrapper.get('[data-node="x"]').trigger('pointerenter')

    expect(wrapper.get('[data-node="x"]').classes()).not.toContain('dim')
    expect(wrapper.get('[data-node="y"]').classes()).not.toContain('dim')
    expect(wrapper.get('[data-node="hub"]').classes()).toContain('dim')
    expect(wrapper.find('[data-label="y"]').exists()).toBe(true)
    expect(wrapper.get('[data-edge="contradicts"]').classes()).not.toContain('dim')
    expect(wrapper.get('[data-edge="dependsOn"]').classes()).toContain('dim')

    await wrapper.get('[data-node="x"]').trigger('pointerleave')
    expect(wrapper.findAll('.dim')).toHaveLength(0)
  })

  it('shows the same highlight on keyboard focus', async () => {
    const wrapper = await mountCanvas()

    await wrapper.get('[data-node="x"]').trigger('focus')

    expect(wrapper.get('[data-node="hub"]').classes()).toContain('dim')
    expect(wrapper.find('[data-label="y"]').exists()).toBe(true)
    await wrapper.get('[data-node="x"]').trigger('blur')
    expect(wrapper.findAll('.dim')).toHaveLength(0)
  })

  it('opens a chunk on click and on Enter, but not after a drag', async () => {
    const wrapper = await mountCanvas()
    const target = wrapper.get('[data-node="s05"]')

    await target.trigger('click')
    await target.trigger('keydown', { key: 'Enter' })
    pointer(target.element, 'pointerdown', 100, 100)
    pointer(target.element, 'pointermove', 112, 100)
    pointer(target.element, 'pointerup', 112, 100)
    await target.trigger('click')
    pointer(target.element, 'pointerdown', 100, 100)
    pointer(target.element, 'pointermove', 102, 101)
    pointer(target.element, 'pointerup', 102, 101)
    await target.trigger('click')

    expect(wrapper.emitted('open')).toEqual([['s05'], ['s05'], ['s05']])
  })

  it('zooms in, out and back to fit with the on-screen controls', async () => {
    const wrapper = await mountCanvas()
    const current = () => scale(wrapper.get('[data-viewport]').attributes('transform'))
    const fit = current()

    await wrapper.get('[data-zoom="in"]').trigger('click')
    expect(current()).toBeGreaterThan(fit)
    await wrapper.get('[data-zoom="out"]').trigger('click')
    await wrapper.get('[data-zoom="out"]').trigger('click')
    expect(current()).toBeLessThan(fit)
    await wrapper.get('[data-zoom="fit"]').trigger('click')
    expect(current()).toBeCloseTo(fit)
  })

  it('marks a centre node and can hide the zoom controls', async () => {
    const wrapper = await mountCanvas({ centreId: 'hub', controls: false })

    expect(wrapper.get('[data-node="hub"]').attributes('data-centre')).toBe('true')
    expect(wrapper.find('[data-zoom]').exists()).toBe(false)
  })
})
