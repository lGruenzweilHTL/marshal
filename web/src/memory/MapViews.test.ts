import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import GraphView from './GraphView.vue'
import MapView from './MapView.vue'
import ScatterView from './ScatterView.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

const printer = 'PRINTERPRINTERPRINTERPRINT'
const nozzle = 'NOZZLENOZZLENOZZLENOZZLENO'
const graph = {
  nodes: [
    { id: printer, title: 'Printer', type: 'device', projectId: null, pinned: true, importance: 4 },
    { id: nozzle, title: 'Nozzle', type: 'device', projectId: null, pinned: false, importance: 3 },
  ],
  edges: [{ from: nozzle, to: printer, kind: 'dependsOn', createdAt: '2026-09-21T10:00:00Z' }],
}
const scatter = [
  { id: printer, title: 'Printer', type: 'device', projectId: null, x: 1.5, y: -0.5 },
  { id: nozzle, title: 'Nozzle', type: 'fact', projectId: null, x: -1, y: 0.25 },
]

// The graph canvas loads lazily: the dynamic import, then the data.
async function settle() {
  for (let round = 0; round < 3; round++) {
    await flushPromises()
    await vi.dynamicImportSettled()
  }
  await flushPromises()
}

async function mountView(component: typeof GraphView | typeof ScatterView, path: string, route: string, body: unknown, projects: { status: number; body: unknown } = { status: 200, body: [] }) {
  const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', route, 200, body).on('GET', '/api/v1/memory/projects', projects.status, projects.body)
  const { options, router } = await mountOptions(fake, path)
  const wrapper = mount(component, options)
  await settle()
  return { wrapper, router }
}

describe('GraphView', () => {
  it('draws a node per chunk and a line per edge, opening a chunk on click', async () => {
    const { wrapper, router } = await mountView(GraphView, '/memory/graph', '/api/v1/memory/graph', graph)

    expect(wrapper.findAll('[data-node]')).toHaveLength(2)
    expect(wrapper.findAll('[data-edge]')).toHaveLength(1)
    expect(wrapper.get(`[data-label="${printer}"]`).text()).toBe('Printer')
    expect(wrapper.get('svg').attributes('viewBox')).toBeTruthy()

    await wrapper.get(`[data-node="${nozzle}"]`).trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe(`/memory/chunks/${nozzle}`)
  })

  it('says so when there is nothing to draw', async () => {
    const { wrapper } = await mountView(GraphView, '/memory/graph', '/api/v1/memory/graph', { nodes: [], edges: [] })

    expect(wrapper.text()).toContain('No memory yet')
    expect(wrapper.find('svg[role="img"]').exists()).toBe(false)
  })

  const voron = '0199bbbb-0000-7000-8000-000000000001'
  const grouped = {
    nodes: [
      { id: printer, title: 'Printer', type: 'device', projectId: voron, pinned: true, importance: 4 },
      { id: nozzle, title: 'Nozzle', type: 'device', projectId: voron, pinned: false, importance: 3 },
      { id: 'COFFEECOFFEECOFFEECOFFEECO', title: 'Coffee', type: 'preference', projectId: null, pinned: false, importance: 2 },
      { id: 'BIRTHDAYBIRTHDAYBIRTHDAYBI', title: 'Birthday', type: 'event', projectId: null, pinned: false, importance: 2 },
    ],
    edges: [
      { from: nozzle, to: printer, kind: 'dependsOn', createdAt: '2026-09-21T10:00:00Z' },
      { from: 'BIRTHDAYBIRTHDAYBIRTHDAYBI', to: 'COFFEECOFFEECOFFEECOFFEECO', kind: 'relatesTo', createdAt: '2026-09-21T10:00:00Z' },
    ],
  }
  const projects = [{ id: voron, name: 'Voron', description: '', createdAt: '2026-09-01T10:00:00Z', lastActiveAt: null, chunkCount: 2, aliases: [] }]

  it('draws a named backdrop per project and none for chunks without one', async () => {
    const { wrapper } = await mountView(GraphView, '/memory/map', '/api/v1/memory/graph', grouped, { status: 200, body: projects })

    const backdrops = wrapper.findAll('[data-backdrop]')
    expect(backdrops.map((backdrop) => backdrop.attributes('data-backdrop'))).toEqual([voron])
    expect(backdrops[0]!.text()).toContain('Voron')
  })

  it('draws backdrops without names when the projects cannot be loaded', async () => {
    const { wrapper } = await mountView(GraphView, '/memory/map', '/api/v1/memory/graph', grouped, { status: 500, body: { title: 'Broken' } })

    expect(wrapper.findAll('[data-node]')).toHaveLength(4)
    expect(wrapper.get(`[data-backdrop="${voron}"]`).text()).toBe('')
  })

  it('turns a type off and on from the legend, keeping the choice in the query', async () => {
    const { wrapper, router } = await mountView(GraphView, '/memory/map', '/api/v1/memory/graph', grouped)
    const chip = () => wrapper.get('[data-type-toggle="event"]')

    expect(chip().attributes('aria-pressed')).toBe('true')
    await chip().trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ hide: 'event' })
    expect(chip().attributes('aria-pressed')).toBe('false')
    await chip().trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.query).toEqual({})
  })

  it('leaves out hidden types and their edges', async () => {
    const { wrapper } = await mountView(GraphView, '/memory/map?hide=event,fact', '/api/v1/memory/graph', grouped)

    expect(wrapper.findAll('[data-node]')).toHaveLength(3)
    expect(wrapper.find('[data-node="BIRTHDAYBIRTHDAYBIRTHDAYBI"]').exists()).toBe(false)
    expect(wrapper.findAll('[data-edge]').map((edge) => edge.attributes('data-edge'))).toEqual(['dependsOn'])
  })

  it('keeps the last shown type from being turned off', async () => {
    const { wrapper } = await mountView(GraphView, '/memory/map?hide=fact,preference,event,procedure,person', '/api/v1/memory/graph', grouped)

    expect(wrapper.get('[data-type-toggle="device"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-type-toggle="fact"]').attributes('disabled')).toBeUndefined()
  })
})

describe('ScatterView', () => {
  it('plots a point per chunk coloured by type, opening a chunk on click', async () => {
    const { wrapper, router } = await mountView(ScatterView, '/memory/scatter', '/api/v1/memory/scatter', scatter)

    const points = wrapper.findAll('[data-point]')
    expect(points).toHaveLength(2)
    expect(wrapper.get(`[data-point="${printer}"]`).attributes('data-type')).toBe('device')
    expect(wrapper.text()).toContain('fact')

    await wrapper.get(`[data-point="${printer}"]`).trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe(`/memory/chunks/${printer}`)
  })

  it('says so when there is nothing to plot', async () => {
    const { wrapper } = await mountView(ScatterView, '/memory/scatter', '/api/v1/memory/scatter', [])

    expect(wrapper.text()).toContain('No memory yet')
  })
})

describe('MapView', () => {
  async function mountMap(path: string) {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/memory/graph', 200, graph).on('GET', '/api/v1/memory/scatter', 200, scatter)
    const { options, router } = await mountOptions(fake, path)
    const wrapper = mount(MapView, options)
    await settle()
    return { wrapper, router }
  }

  it('toggles between graph and scatter on desktop and keeps the choice in the query', async () => {
    useViewport('desktop')
    const { wrapper, router } = await mountMap('/memory/map')

    expect(wrapper.findAll('[data-node]')).toHaveLength(2)
    await wrapper.get('[data-value="scatter"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ view: 'scatter' })
    expect(wrapper.findAll('[data-point]')).toHaveLength(2)
  })

  it('lists memory grouped by type on phones', async () => {
    useViewport('phone')
    const { wrapper } = await mountMap('/memory/map')

    expect(wrapper.text()).toContain('The map needs a larger screen')
    expect(wrapper.find('svg[role="img"]').exists()).toBe(false)
    expect(wrapper.get('[data-group="device"]').text()).toContain('Printer')
    expect(wrapper.get(`a[href="/memory/chunks/${nozzle}"]`).text()).toContain('Nozzle')
  })
})
