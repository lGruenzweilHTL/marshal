import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import ArtifactsView from './ArtifactsView.vue'
import { FakeServer } from '../testing/fakeServer'
import { artifact } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

const list = (items: unknown[]) => ({ items, usedBytes: 3 * 1024 * 1024, quotaBytes: 5 * 1024 * 1024 * 1024 })

function server(items: unknown[] = [artifact(), artifact({ id: 'a2', name: 'notes.md', kind: 'markdown', source: 'upload', sourceReference: null, tainted: false, size: 200 })]) {
  return new FakeServer().antiforgery('csrf').on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/artifacts', 200, list(items))
}

async function mountList(fake: FakeServer, language?: 'en' | 'de') {
  const mounted = await mountOptions(fake, '/artifacts', language ? { language } : undefined)
  const wrapper = mount(ArtifactsView, { ...mounted.options, attachTo: document.body })
  await flushPromises()
  return { ...mounted, wrapper }
}

const dialog = () => document.body.querySelector<HTMLElement>('[role="dialog"]')

afterEach(() => {
  document.body.innerHTML = ''
})

describe('ArtifactsView', () => {
  it('lists artifacts in server order with kind, source, size and the tainted marker, and the quota usage', async () => {
    const { wrapper } = await mountList(server())

    const rows = wrapper.findAll('[data-artifact]')
    expect(rows.map((row) => row.attributes('data-artifact'))).toEqual(['a1', 'a2'])
    expect(rows[0]!.text()).toContain('bracket.stl')
    expect(rows[0]!.text()).toContain('3D model · Coding session · 1.5 KB')
    expect(rows[0]!.find('[data-test="tainted"]').exists()).toBe(true)
    expect(rows[1]!.find('[data-test="tainted"]').exists()).toBe(false)
    expect(rows[0]!.attributes('href')).toBe('/artifacts/a1')
    expect(wrapper.get('[data-test="usage"]').text()).toBe('3 MB of 5 GB used')
  })

  it('sends kind and source filters and the search', async () => {
    const fake = server()
    const { wrapper } = await mountList(fake)

    await wrapper.get('select[name="kind"]').setValue('model3d')
    await wrapper.get('select[name="source"]').setValue('mail')
    await flushPromises()
    expect(fake.requests.findLast((request) => request.path === '/api/v1/artifacts')!.search).toBe('?kind=model3d&source=mail')

    await wrapper.get('input[name="query"]').setValue('brack')
    await new Promise((resolve) => setTimeout(resolve, 300))
    await flushPromises()
    expect(fake.requests.findLast((request) => request.path === '/api/v1/artifacts')!.search).toBe('?kind=model3d&source=mail&query=brack')
  })

  it('uploads the picked file with progress and refreshes', async () => {
    let progressSeen = false
    const fake = server().handle('POST', '/api/v1/artifacts', async (request) => {
      await nextTick()
      progressSeen = document.body.querySelector('[data-test="progress"]') !== null
      expect(request.headers.get('X-XSRF-TOKEN')).toBe('csrf')
      expect(request.body).toContain('filename="cube.stl"')
      return new Response(JSON.stringify(artifact({ id: 'a3', name: 'cube.stl', source: 'upload', tainted: false })), { status: 201, headers: { 'Content-Type': 'application/json' } })
    })
    const { wrapper, toasts } = await mountList(fake)

    const input = wrapper.get('input[type="file"]')
    Object.defineProperty(input.element, 'files', { value: [new File(['solid cube'], 'cube.stl')], configurable: true })
    await input.trigger('change')
    await flushPromises()

    expect(progressSeen).toBe(true)
    expect(wrapper.find('[data-test="progress"]').exists()).toBe(false)
    expect(toasts.items.value.map((toast) => toast.message)).toEqual(['Uploaded cube.stl.'])
    expect(fake.count('GET', '/api/v1/artifacts')).toBe(2)
  })

  it('deletes only after the confirmation dialog', async () => {
    const fake = server().on('DELETE', '/api/v1/artifacts/a1', 204)
    const { wrapper } = await mountList(fake)

    await wrapper.get('[data-artifact="a1"] [data-test="delete"]').trigger('click')
    await flushPromises()
    expect(fake.count('DELETE', '/api/v1/artifacts/a1')).toBe(0)
    expect(dialog()!.textContent).toContain('Delete “bracket.stl”?')

    dialog()!.querySelector<HTMLElement>('[data-test="confirm-delete"]')!.click()
    await flushPromises()
    expect(fake.count('DELETE', '/api/v1/artifacts/a1')).toBe(1)
    expect(fake.count('GET', '/api/v1/artifacts')).toBe(2)
  })

  it('reloads when the live topic reports created or deleted artifacts', async () => {
    const fake = server()
    const { live } = await mountList(fake)

    live.publish('artifacts', 'artifact.created', { id: 'a9', name: 'x.png', kind: 'image', source: 'upload' })
    live.publish('artifacts', 'artifact.deleted', { id: 'a1' })
    await flushPromises()

    expect(fake.count('GET', '/api/v1/artifacts')).toBe(3)
  })

  it('shows the empty state and German texts', async () => {
    const { wrapper } = await mountList(server([]), 'de')

    expect(wrapper.text()).toContain('Noch keine Artefakte')
    expect(wrapper.text()).toContain('Hochladen')
  })
})
