import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
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

async function mountView(component: typeof GraphView | typeof ScatterView, path: string, route: string, body: unknown) {
  const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', route, 200, body)
  const { options, router } = await mountOptions(fake, path)
  const wrapper = mount(component, options)
  await flushPromises()
  return { wrapper, router }
}

describe('GraphView', () => {
  it('draws a node per chunk and a line per edge, opening a chunk on click', async () => {
    const { wrapper, router } = await mountView(GraphView, '/memory/graph', '/api/v1/memory/graph', graph)

    expect(wrapper.findAll('[data-node]')).toHaveLength(2)
    expect(wrapper.findAll('[data-edge]')).toHaveLength(1)
    expect(wrapper.get(`[data-node="${printer}"]`).text()).toContain('Printer')
    expect(wrapper.get('svg').attributes('viewBox')).toBeTruthy()

    await wrapper.get(`[data-node="${nozzle}"]`).trigger('click')
    await flushPromises()
    expect(router.currentRoute.value.fullPath).toBe(`/memory/chunks/${nozzle}`)
  })

  it('says so when there is nothing to draw', async () => {
    const { wrapper } = await mountView(GraphView, '/memory/graph', '/api/v1/memory/graph', { nodes: [], edges: [] })

    expect(wrapper.text()).toContain('No memory yet')
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
    await flushPromises()
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
