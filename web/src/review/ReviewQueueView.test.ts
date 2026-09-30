import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ReviewQueueView from './ReviewQueueView.vue'
import { FakeServer } from '../testing/fakeServer'
import { approvalItem, proposal } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server() {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .antiforgery('xsrf')
    .on('GET', '/api/v1/approvals', 200, [approvalItem(), approvalItem({ id: 'a-2', summary: 'Start the print', tool: 'printer.start_print', taint: [], reasons: [{ code: 'rule', sentence: 'Starting a print needs your approval.' }] })])
    .on('GET', '/api/v1/proposals', 200, [proposal()])
    .on('GET', '/api/v1/proposals/p-1', 200, proposal({ diff: 'diff --git a/feature.txt b/feature.txt\n--- /dev/null\n+++ b/feature.txt\n@@ -0,0 +1 @@\n-old line\n+change for demo\n' }))
}

async function mountReview(fake: FakeServer, path = '/review') {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(ReviewQueueView, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('ReviewQueueView', () => {
  it('shows approvals with the frozen action, reasons verbatim, expiry and a link to the tainting step', async () => {
    const { wrapper } = await mountReview(server())

    const approval = wrapper.get('[data-approval="a-1"]')
    expect(approval.text()).toContain('Unlock the front door')
    expect(approval.text()).toContain('lock.unlock')
    expect(approval.text()).toContain('"door": "front"')
    expect(approval.findAll('[data-test="reason"]').map((reason) => reason.text())).toEqual([
      'Unlocking needs your approval.',
      'This run read an email, which could contain injected instructions.',
    ])
    expect(approval.text()).toContain('Expires')
    const taint = approval.get('[data-test="taint"] a')
    expect(taint.text()).toContain('mail')
    expect(taint.attributes('href')).toBe('/runs/r-1#step-2')
  })

  it('approves and rejects, and removes decided approvals', async () => {
    const fake = server()
      .on('POST', '/api/v1/approvals/a-1/approve', 200, { id: 'a-1', status: 'approved' })
      .on('POST', '/api/v1/approvals/a-2/reject', 200, { id: 'a-2', status: 'rejected' })
    const { wrapper } = await mountReview(fake)

    await wrapper.get('[data-approval="a-1"] [data-test="approve"]').trigger('click')
    await wrapper.get('[data-approval="a-2"] [data-test="reject"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/approvals/a-1/approve')).toBe(1)
    expect(fake.request('POST', '/api/v1/approvals/a-1/approve')!.headers.get('X-XSRF-TOKEN')).toBe('xsrf')
    expect(fake.count('POST', '/api/v1/approvals/a-2/reject')).toBe(1)
    expect(wrapper.find('[data-approval="a-1"]').exists()).toBe(false)
    expect(wrapper.find('[data-approval="a-2"]').exists()).toBe(false)
  })

  it('shows why a decision failed and keeps the approval', async () => {
    const fake = server().on('POST', '/api/v1/approvals/a-1/approve', 409, { title: 'Conflict', detail: 'The approval was already decided.' })
    const { wrapper } = await mountReview(fake)

    await wrapper.get('[data-approval="a-1"] [data-test="approve"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-approval="a-1"] [role="alert"]').text()).toBe('The approval was already decided.')
  })

  it('highlights the approval a push deep link points to', async () => {
    const { wrapper } = await mountReview(server(), '/review/a-2')

    expect(wrapper.get('[data-approval="a-2"]').classes()).toContain('focused')
    expect(wrapper.get('[data-approval="a-1"]').classes()).not.toContain('focused')
  })

  it('shows coding proposals and loads the diff on request', async () => {
    const fake = server()
    const { wrapper } = await mountReview(fake)

    const card = wrapper.get('[data-proposal="p-1"]')
    expect(card.text()).toContain('Add a feature file')
    expect(card.text()).toContain('demo')
    expect(card.text()).toContain('marshal/abc')
    await card.get('[data-test="show-diff"]').trigger('click')
    await flushPromises()

    expect(fake.count('GET', '/api/v1/proposals/p-1')).toBe(1)
    expect(wrapper.get('[data-proposal="p-1"] .diff .added').text()).toBe('+change for demo')
    expect(wrapper.get('[data-proposal="p-1"] .diff .removed').text()).toBe('-old line')
  })

  it('merges a proposal', async () => {
    const fake = server().on('POST', '/api/v1/proposals/p-1/merge', 200, proposal({ status: 'merged' }))
    const { wrapper } = await mountReview(fake)

    await wrapper.get('[data-proposal="p-1"] [data-test="merge"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/proposals/p-1/merge')).toBe(1)
    expect(wrapper.find('[data-proposal="p-1"]').exists()).toBe(false)
  })

  it('keeps a proposal whose merge conflicts and says why', async () => {
    const fake = server().on('POST', '/api/v1/proposals/p-1/merge', 409, { title: 'Conflict', detail: 'The branch no longer merges cleanly.' })
    const { wrapper } = await mountReview(fake)

    await wrapper.get('[data-proposal="p-1"] [data-test="merge"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-proposal="p-1"] [role="alert"]').text()).toBe('The branch no longer merges cleanly.')
  })

  it('rejects a proposal', async () => {
    const fake = server().on('POST', '/api/v1/proposals/p-1/reject', 200, proposal({ status: 'rejected' }))
    const { wrapper } = await mountReview(fake)

    await wrapper.get('[data-proposal="p-1"] [data-test="reject"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/proposals/p-1/reject')).toBe(1)
    expect(wrapper.find('[data-proposal="p-1"]').exists()).toBe(false)
  })

  it('says when nothing waits for review', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/approvals', 200, []).on('GET', '/api/v1/proposals', 200, [])
    const { wrapper } = await mountReview(fake)

    expect(wrapper.text()).toContain('Nothing waits for your review.')
  })
})
