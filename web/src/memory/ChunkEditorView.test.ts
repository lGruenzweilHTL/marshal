import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import ChunkEditorView from './ChunkEditorView.vue'
import { chunk, project, projectId } from './fixtures'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const id = 'AAAAAAAAAAAAAAAAAAAAAAAAAA'
const detail = {
  chunk: chunk(),
  versions: [{ chunkId: id, content: '0.5 mm nozzle.', importance: 3, sourceRef: null, replacedAt: '2026-09-21T10:00:00Z' }],
  edges: [{ from: id, to: 'PPPPPPPPPPPPPPPPPPPPPPPPPP', kind: 'supersedes', createdAt: '2026-09-20T10:00:00Z' }],
}
const history = [
  { chunkId: 'PPPPPPPPPPPPPPPPPPPPPPPPPP', kind: 'predecessor', title: 'Printer nozzle', content: '0.4 mm.', importance: 3, validFrom: '2026-09-01T10:00:00Z' },
  { chunkId: id, kind: 'version', title: 'Voron nozzle', content: '0.5 mm nozzle.', importance: 3, validFrom: '2026-09-20T10:00:00Z' },
  { chunkId: id, kind: 'current', title: 'Voron nozzle', content: '0.6 mm nozzle.', importance: 3, validFrom: '2026-09-21T10:00:00Z' },
]

function server() {
  return new FakeServer()
    .antiforgery('t')
    .on('GET', '/api/v1/me', 200, me)
    .on('GET', '/api/v1/memory/projects', 200, [project()])
    .on('GET', `/api/v1/memory/chunks/${id}`, 200, detail)
    .on('GET', `/api/v1/memory/chunks/${id}/history`, 200, history)
}

async function mountEditor(fake: FakeServer, path = `/memory/chunks/${id}`) {
  const { options, router } = await mountOptions(fake, path)
  const wrapper = mount(ChunkEditorView, { ...options, attachTo: document.body })
  await flushPromises()
  return { wrapper, router }
}

describe('ChunkEditorView', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('creates a chunk and opens it', async () => {
    const fake = server().on('POST', '/api/v1/memory/chunks', 201, { id, outcome: 'created' })
    const { wrapper, router } = await mountEditor(fake, '/memory/new')

    await wrapper.get('select[name="scope"]').setValue('project')
    await wrapper.get('select[name="projectId"]').setValue(projectId)
    await wrapper.get('select[name="type"]').setValue('procedure')
    await wrapper.get('input[name="title"]').setValue('Calibration')
    await wrapper.get('textarea[name="content"]').setValue('Run the z offset macro.')
    await wrapper.get('[data-test="importance"] [data-value="4"]').trigger('click')
    await wrapper.get('form[data-test="chunk"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/memory/chunks')!.body)).toEqual({
      scope: 'project',
      projectId,
      type: 'procedure',
      title: 'Calibration',
      content: 'Run the z offset macro.',
      importance: 4,
      sourceRef: null,
    })
    expect(router.currentRoute.value.fullPath).toBe(`/memory/chunks/${id}`)
  })

  it('edits content and importance of an existing chunk', async () => {
    const fake = server().on('PUT', `/api/v1/memory/chunks/${id}`, 200, { id, outcome: 'updated' })
    const { wrapper } = await mountEditor(fake)

    expect((wrapper.get('input[name="title"]').element as HTMLInputElement).disabled).toBe(true)
    await wrapper.get('textarea[name="content"]').setValue('0.8 mm nozzle.')
    await wrapper.get('form[data-test="chunk"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('PUT', `/api/v1/memory/chunks/${id}`)!.body)).toEqual({ content: '0.8 mm nozzle.', importance: 3, sourceRef: null })
    expect(wrapper.text()).toContain('Saved')
  })

  it('pins and unpins', async () => {
    const fake = server().on('PUT', `/api/v1/memory/chunks/${id}/pin`, 204)
    const { wrapper } = await mountEditor(fake)

    await wrapper.get('[data-test="pin"] input').setValue(true)
    await flushPromises()

    expect(fake.count('PUT', `/api/v1/memory/chunks/${id}/pin`)).toBe(1)
    expect(fake.count('GET', `/api/v1/memory/chunks/${id}`)).toBe(2)
  })

  it('shows predecessors, versions and the current content in order', async () => {
    const { wrapper } = await mountEditor(server())

    const entries = wrapper.findAll('[data-history]')
    expect(entries.map((entry) => entry.attributes('data-history'))).toEqual(['predecessor', 'version', 'current'])
    expect(entries[0]!.text()).toContain('0.4 mm.')
    expect(entries[0]!.get('a').attributes('href')).toBe('/memory/chunks/PPPPPPPPPPPPPPPPPPPPPPPPPP')
    expect(entries[2]!.text()).toContain('0.6 mm nozzle.')
  })

  it('supersedes with a new identity and opens the successor', async () => {
    const successor = 'SSSSSSSSSSSSSSSSSSSSSSSSSS'
    const fake = server().on('POST', `/api/v1/memory/chunks/${id}/supersede`, 201, { id: successor, outcome: 'created' })
    const { wrapper, router } = await mountEditor(fake)

    await wrapper.get('[data-test="show-supersede"]').trigger('click')
    await wrapper.get('form[data-test="supersede"] input[name="newTitle"]').setValue('Voron hotend nozzle')
    await wrapper.get('form[data-test="supersede"] textarea[name="newContent"]').setValue('0.6 mm hardened steel.')
    await wrapper.get('form[data-test="supersede"]').trigger('submit')
    await flushPromises()

    const body = JSON.parse(fake.request('POST', `/api/v1/memory/chunks/${id}/supersede`)!.body)
    expect(body).toMatchObject({ scope: 'general', type: 'device', title: 'Voron hotend nozzle', content: '0.6 mm hardened steel.' })
    expect(router.currentRoute.value.fullPath).toBe(`/memory/chunks/${successor}`)
  })

  it('deletes after confirmation and returns to the list', async () => {
    const fake = server().on('DELETE', `/api/v1/memory/chunks/${id}`, 204)
    const { wrapper, router } = await mountEditor(fake)

    await wrapper.get('[data-test="delete"]').trigger('click')
    expect(fake.count('DELETE', `/api/v1/memory/chunks/${id}`)).toBe(0)
    ;(document.body.querySelector('[data-test="confirm-delete"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(fake.count('DELETE', `/api/v1/memory/chunks/${id}`)).toBe(1)
    expect(router.currentRoute.value.fullPath).toBe('/memory')
  })

  it('shows validation problems', async () => {
    const fake = server().on('PUT', `/api/v1/memory/chunks/${id}`, 400, { title: 'Invalid', detail: 'The content must have 1–4000 characters.', code: 'memory.invalid_chunk' })
    const { wrapper } = await mountEditor(fake)

    await wrapper.get('form[data-test="chunk"]').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('The content must have')
    expect((wrapper.get('textarea[name="content"]').element as HTMLTextAreaElement).value).toBe('0.6 mm nozzle.')
  })

  it('reports a missing chunk', async () => {
    const fake = server().on('GET', `/api/v1/memory/chunks/${id}`, 404, { status: 404, code: 'memory.chunk_not_found' })
    const { wrapper } = await mountEditor(fake)

    expect(wrapper.text()).toContain('This memory chunk does not exist.')
  })
})
