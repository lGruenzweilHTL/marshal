import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import ProjectsView from './ProjectsView.vue'
import { project, projectId } from './fixtures'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const aliasId = '0199cccc-0000-7000-8000-000000000001'
const voron = project({
  aliases: [
    { id: '0199cccc-0000-7000-8000-000000000002', phrase: 'the printer', status: 'active', confirmations: 0, createdAt: '2026-09-01T10:00:00Z' },
    { id: aliasId, phrase: 'the loud thing', status: 'candidate', confirmations: 2, createdAt: '2026-09-02T10:00:00Z' },
  ],
})
const base = `/api/v1/memory/projects/${projectId}`

function server() {
  return new FakeServer().antiforgery('t').on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/memory/projects', 200, [voron])
}

async function mountProjects(fake: FakeServer) {
  const { options } = await mountOptions(fake, '/memory/projects')
  const wrapper = mount(ProjectsView, { ...options, attachTo: document.body })
  await flushPromises()
  return wrapper
}

describe('ProjectsView', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('lists projects with aliases and candidates', async () => {
    const wrapper = await mountProjects(server())

    const card = wrapper.get(`[data-project="${projectId}"]`)
    expect(card.text()).toContain('Voron')
    expect(card.text()).toContain('The 3D printer.')
    expect(card.get('[data-alias="the printer"]').text()).toContain('active')
    expect(card.get('[data-alias="the loud thing"]').text()).toContain('candidate (2 confirmations)')
  })

  it('creates a project', async () => {
    const fake = server().on('POST', '/api/v1/memory/projects', 201, project({ id: '0199bbbb-0000-7000-8000-000000000002', name: 'Marshal' }))
    const wrapper = await mountProjects(fake)

    await wrapper.get('[data-test="show-new"]').trigger('click')
    await wrapper.get('form[data-test="new-project"] input[name="name"]').setValue('Marshal')
    await wrapper.get('form[data-test="new-project"] textarea[name="description"]').setValue('The voice assistant.')
    await wrapper.get('form[data-test="new-project"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('POST', '/api/v1/memory/projects')!.body)).toEqual({ name: 'Marshal', description: 'The voice assistant.' })
    expect(fake.count('GET', '/api/v1/memory/projects')).toBe(2)
  })

  it('promotes, rejects and adds aliases', async () => {
    const fake = server()
      .on('PUT', `${base}/aliases/${aliasId}`, 200, {})
      .on('POST', `${base}/aliases`, 201, {})
    const wrapper = await mountProjects(fake)

    await wrapper.get('[data-alias="the loud thing"] [data-test="reject"]').trigger('click')
    await flushPromises()
    expect(JSON.parse(fake.request('PUT', `${base}/aliases/${aliasId}`)!.body)).toEqual({ status: 'rejected' })

    await wrapper.get('[data-alias="the loud thing"] [data-test="promote"]').trigger('click')
    await flushPromises()
    expect(JSON.parse(fake.request('PUT', `${base}/aliases/${aliasId}`)!.body)).toEqual({ status: 'active' })

    await wrapper.get(`[data-project="${projectId}"] input[name="alias"]`).setValue('the voron')
    await wrapper.get(`[data-project="${projectId}"] form[data-test="add-alias"]`).trigger('submit')
    await flushPromises()
    expect(JSON.parse(fake.request('POST', `${base}/aliases`)!.body)).toEqual({ phrase: 'the voron' })
  })

  it('shows the correction log on demand', async () => {
    const fake = server().on('GET', `${base}/feedback`, 200, [
      { id: '0199dddd-0000-7000-8000-000000000001', kind: 'rejected', phrase: 'the printer thing', runId: null, createdAt: '2026-09-21T10:00:00Z' },
    ])
    const wrapper = await mountProjects(fake)

    await wrapper.get(`[data-project="${projectId}"] [data-test="feedback"]`).trigger('click')
    await flushPromises()

    expect(wrapper.get(`[data-project="${projectId}"] [data-test="feedback-log"]`).text()).toContain('rejected')
    expect(wrapper.get(`[data-project="${projectId}"] [data-test="feedback-log"]`).text()).toContain('the printer thing')
  })

  it('renames and deletes a project', async () => {
    const fake = server().on('PUT', base, 200, voron).on('DELETE', base, 204)
    const wrapper = await mountProjects(fake)

    await wrapper.get(`[data-project="${projectId}"] [data-test="edit"]`).trigger('click')
    await wrapper.get(`[data-project="${projectId}"] form[data-test="edit-project"] input[name="name"]`).setValue('Voron 2.4')
    await wrapper.get(`[data-project="${projectId}"] form[data-test="edit-project"]`).trigger('submit')
    await flushPromises()
    await wrapper.get(`[data-project="${projectId}"] [data-test="delete"]`).trigger('click')
    expect(fake.count('DELETE', base)).toBe(0)
    ;(document.body.querySelector('[data-test="confirm-delete"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(JSON.parse(fake.request('PUT', base)!.body)).toEqual({ name: 'Voron 2.4', description: 'The 3D printer.' })
    expect(fake.count('DELETE', base)).toBe(1)
  })
})
