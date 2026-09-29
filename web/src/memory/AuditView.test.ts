import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AuditView from './AuditView.vue'
import { chunk } from './fixtures'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const six = chunk({ id: 'SIXSIXSIXSIXSIXSIXSIXSIXSI', title: 'Voron nozzle', content: '0.6 mm.' })
const four = chunk({ id: 'FOURFOURFOURFOURFOURFOURFO', title: 'Voron nozzle (conflicting)', content: '0.4 mm.' })
const report = {
  stale: [chunk({ id: 'OLDOLDOLDOLDOLDOLDOLDOLDOL', title: 'Old router', updatedAt: '2026-01-01T10:00:00Z' })],
  contradictions: [{ from: four, to: six, since: '2026-09-21T10:00:00Z' }],
  orphans: [chunk({ id: 'ORPHANORPHANORPHANORPHANOR', title: 'Calibration', scope: 'project', projectId: '0199bbbb-0000-7000-8000-000000000009' })],
  danglingEdges: [{ from: 'LIVELIVELIVELIVELIVELIVELI', to: 'GONEGONEGONEGONEGONEGONEGO', kind: 'relatesTo', createdAt: '2026-09-21T10:00:00Z' }],
}

async function mountAudit(body: unknown) {
  const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/memory/audit', 200, body)
  const { options } = await mountOptions(fake, '/memory/audit')
  const wrapper = mount(AuditView, options)
  await flushPromises()
  return wrapper
}

describe('AuditView', () => {
  it('lists stale chunks, contradiction pairs, orphans and dangling edges', async () => {
    const wrapper = await mountAudit(report)

    expect(wrapper.get('[data-section="stale"]').text()).toContain('Old router')
    const pair = wrapper.get('[data-section="contradictions"] li')
    expect(pair.text()).toContain('0.4 mm.')
    expect(pair.text()).toContain('0.6 mm.')
    expect(pair.findAll('a').map((link) => link.attributes('href'))).toEqual(['/memory/chunks/FOURFOURFOURFOURFOURFOURFO', '/memory/chunks/SIXSIXSIXSIXSIXSIXSIXSIXSI'])
    expect(wrapper.get('[data-section="orphans"]').text()).toContain('Calibration')
    expect(wrapper.get('[data-section="dangling"]').text()).toContain('relatesTo')
  })

  it('reports a clean memory', async () => {
    const wrapper = await mountAudit({ stale: [], contradictions: [], orphans: [], danglingEdges: [] })

    expect(wrapper.text()).toContain('Nothing to review')
  })
})
