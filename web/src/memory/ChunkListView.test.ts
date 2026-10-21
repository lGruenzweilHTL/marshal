import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import ChunkListView from './ChunkListView.vue'
import { chunk, project, projectId } from './fixtures'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

const chunks = [
  chunk({ pinned: true, title: 'Allergy', type: 'fact', id: 'BBBBBBBBBBBBBBBBBBBBBBBBBB', content: 'Allergic to peanuts.' }),
  chunk({ scope: 'project', projectId, type: 'procedure', title: 'Calibration', id: 'CCCCCCCCCCCCCCCCCCCCCCCCCC' }),
]

function server() {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .on('GET', '/api/v1/memory/chunks', 200, chunks)
    .on('GET', '/api/v1/memory/projects', 200, [project()])
}

async function mountList(fake: FakeServer) {
  const { options, router } = await mountOptions(fake, '/memory')
  const wrapper = mount(ChunkListView, options)
  await flushPromises()
  return { wrapper, router }
}

describe('ChunkListView', () => {
  beforeEach(() => useViewport('desktop'))

  it('lists chunks with type, project and pinned badge, linking to the editor', async () => {
    const { wrapper } = await mountList(server())

    const allergy = wrapper.get('[data-chunk="BBBBBBBBBBBBBBBBBBBBBBBBBB"]')
    expect(allergy.text()).toContain('Allergy')
    expect(allergy.text()).toContain('fact')
    expect(allergy.find('[data-test="pinned"]').exists()).toBe(true)
    expect(allergy.get('a').attributes('href')).toBe('/memory/chunks/BBBBBBBBBBBBBBBBBBBBBBBBBB')
    expect(wrapper.get('[data-chunk="CCCCCCCCCCCCCCCCCCCCCCCCCC"]').text()).toContain('Voron')
  })

  it('searches and filters through the API', async () => {
    const fake = server()
    const { wrapper } = await mountList(fake)

    await wrapper.get('input[name="query"]').setValue('nozzle')
    await wrapper.get('[data-type="device"]').trigger('click')
    await wrapper.get('select[name="project"]').setValue(projectId)
    await wrapper.get('[data-test="pinned-only"]').trigger('click')
    await wrapper.get('[data-test="closed-only"]').trigger('click')
    await wrapper.get('form[data-test="search"]').trigger('submit')
    await flushPromises()

    const search = new URLSearchParams(fake.request('GET', '/api/v1/memory/chunks')!.search)
    expect(search.get('query')).toBe('nozzle')
    expect(search.get('type')).toBe('device')
    expect(search.get('projectId')).toBe(projectId)
    expect(search.get('pinned')).toBe('true')
    expect(search.get('closed')).toBe('true')
  })

  it('marks closed chunks', async () => {
    const closed = chunk({ id: 'DDDDDDDDDDDDDDDDDDDDDDDDDD', title: 'Old workplace', validTo: '2025-03-01T00:00:00Z' })
    const { wrapper } = await mountList(server().on('GET', '/api/v1/memory/chunks', 200, [closed]))

    expect(wrapper.get('[data-chunk="DDDDDDDDDDDDDDDDDDDDDDDDDD"] [data-test="closed"]').text()).toContain('until')
  })

  it('starts filtered by the project from the query', async () => {
    const fake = server()
    const { options } = await mountOptions(fake, `/memory?project=${projectId}`)
    mount(ChunkListView, options)
    await flushPromises()

    expect(new URLSearchParams(fake.request('GET', '/api/v1/memory/chunks')!.search).get('projectId')).toBe(projectId)
  })

  it('says so when memory is empty', async () => {
    const { wrapper } = await mountList(server().on('GET', '/api/v1/memory/chunks', 200, []))

    expect(wrapper.text()).toContain('No memory matches')
  })

  it('lists chunks as rows on phones', async () => {
    useViewport('phone')
    const { wrapper } = await mountList(server())

    expect(wrapper.find('table').exists()).toBe(false)
    expect(wrapper.get('a[href="/memory/chunks/BBBBBBBBBBBBBBBBBBBBBBBBBB"]').text()).toContain('Allergy')
  })
})
