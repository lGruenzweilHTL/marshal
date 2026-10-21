import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import AuditView from './AuditView.vue'
import { chunk } from './fixtures'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const six = chunk({ id: 'SIXSIXSIXSIXSIXSIXSIXSIXSI', title: 'Voron nozzle', content: '0.6 mm.' })
const four = chunk({ id: 'FOURFOURFOURFOURFOURFOURFO', title: 'Voron nozzle (conflicting)', content: '0.4 mm.' })
const proposalId = '0199eeee-0000-7000-8000-000000000001'
const duplicateId = '0199eeee-0000-7000-8000-000000000002'
const proposals = [
  {
    id: proposalId,
    kind: 'contradiction',
    a: four,
    b: six,
    suggestion: { current: six.id, outdated: four.id, closeAt: '2026-09-21T10:00:00Z', reason: 'The 0.6 mm nozzle replaced the 0.4 mm one.' },
    createdAt: '2026-09-22T03:00:00Z',
  },
  {
    id: duplicateId,
    kind: 'duplicate',
    a: chunk({ id: 'DUPADUPADUPADUPADUPADUPADU', title: 'Coffee', content: 'Likes espresso.' }),
    b: chunk({ id: 'DUPBDUPBDUPBDUPBDUPBDUPBDU', title: 'Coffee preference', content: 'Drinks espresso.' }),
    suggestion: { keep: 'DUPADUPADUPADUPADUPADUPADU', other: 'DUPBDUPBDUPBDUPBDUPBDUPBDU', title: 'Coffee', content: 'Likes and drinks espresso.', similarity: 0.94 },
    createdAt: '2026-09-22T03:00:00Z',
  },
]
const report = {
  proposals,
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

  it('shows proposals with their suggestion', async () => {
    const wrapper = await mountAudit(report)

    const contradiction = wrapper.get(`[data-proposal="${proposalId}"]`)
    expect(contradiction.text()).toContain('0.4 mm.')
    expect(contradiction.text()).toContain('The 0.6 mm nozzle replaced the 0.4 mm one.')
    expect(contradiction.get('[data-test="outdated"]').text()).toContain('Voron nozzle (conflicting)')
    const duplicate = wrapper.get(`[data-proposal="${duplicateId}"]`)
    expect(duplicate.text()).toContain('Likes and drinks espresso.')
    expect(duplicate.text()).toContain('94%')
  })

  it('accepts and dismisses proposals, then reloads the audit', async () => {
    const fake = new FakeServer()
      .antiforgery('t')
      .on('GET', '/api/v1/me', 200, me)
      .on('GET', '/api/v1/memory/audit', 200, report)
      .on('POST', `/api/v1/memory/proposals/${proposalId}/accept`, 204)
      .on('POST', `/api/v1/memory/proposals/${duplicateId}/dismiss`, 204)
    const { options } = await mountOptions(fake, '/memory/audit')
    const wrapper = mount(AuditView, options)
    await flushPromises()

    await wrapper.get(`[data-proposal="${proposalId}"] [data-test="accept"]`).trigger('click')
    await flushPromises()
    await wrapper.get(`[data-proposal="${duplicateId}"] [data-test="dismiss"]`).trigger('click')
    await flushPromises()

    expect(fake.count('POST', `/api/v1/memory/proposals/${proposalId}/accept`)).toBe(1)
    expect(fake.count('POST', `/api/v1/memory/proposals/${duplicateId}/dismiss`)).toBe(1)
    expect(fake.count('GET', '/api/v1/memory/audit')).toBe(3)
  })

  it('reports a clean memory', async () => {
    const wrapper = await mountAudit({ stale: [], contradictions: [], orphans: [], danglingEdges: [], proposals: [] })

    expect(wrapper.text()).toContain('Nothing to review')
  })
})
