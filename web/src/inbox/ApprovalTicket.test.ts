import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import ApprovalTicket from './ApprovalTicket.vue'
import { FakeServer } from '../testing/fakeServer'
import { approvalItem } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server() {
  return new FakeServer().on('GET', '/api/v1/me', 200, me).antiforgery('xsrf')
}

async function mountTicket(fake: FakeServer, props: Record<string, unknown> = {}) {
  const mounted = await mountOptions(fake, '/inbox')
  const wrapper = mount(ApprovalTicket, { ...mounted.options, props: { approval: approvalItem(), ...props } })
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('ApprovalTicket', () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date', 'setInterval', 'clearInterval'] })
    vi.setSystemTime(new Date('2026-09-30T08:07:30Z'))
  })
  afterEach(() => vi.useRealTimers())

  it('shows the tool, summary, frozen arguments, reasons and a link to the tainting step', async () => {
    const { wrapper } = await mountTicket(server())

    expect(wrapper.text()).toContain('lock.unlock')
    expect(wrapper.text()).toContain('Unlock the front door')
    expect(wrapper.text()).toContain('"door": "front"')
    expect(wrapper.get('[data-test="reason"]').text()).toBe('Unlocking needs your approval.')
    const taint = wrapper.get('[data-test="taint"] a')
    expect(taint.text()).toBe('mail in step 2')
    expect(taint.attributes('href')).toBe('/runs/r-1#step-2')
  })

  it('counts down to the expiry and turns into the expired state', async () => {
    const { wrapper } = await mountTicket(server())
    expect(wrapper.get('[data-test="ttl"]').text()).toBe('2:30')

    vi.advanceTimersByTime(150_000)
    await flushPromises()

    expect(wrapper.find('[data-test="approve"]').exists()).toBe(false)
    expect(wrapper.get('[data-test="result"]').text()).toContain('Expired')
  })

  it('compact density leaves out the arguments', async () => {
    const { wrapper } = await mountTicket(server(), { density: 'compact' })

    expect(wrapper.text()).not.toContain('"door": "front"')
    expect(wrapper.find('[data-test="approve"]').exists()).toBe(true)
  })

  it('approves and reports the decision', async () => {
    const fake = server().on('POST', '/api/v1/approvals/a-1/approve', 200, { id: 'a-1', status: 'approved' })
    const { wrapper } = await mountTicket(fake)

    await wrapper.get('[data-test="approve"]').trigger('click')
    await flushPromises()

    expect(fake.requests.some((request) => request.method === 'POST' && request.path === '/api/v1/approvals/a-1/approve')).toBe(true)
    expect(wrapper.get('[data-test="result"]').text()).toContain('Approved')
    expect(wrapper.emitted('decided')).toEqual([['approved']])
  })

  it('keeps a failed decision on screen with the actions', async () => {
    const fake = server().on('POST', '/api/v1/approvals/a-1/reject', 409, {
      status: 409,
      code: 'approval.task_busy',
      detail: 'The task is still finishing its run; try again in a moment.',
    })
    const { wrapper } = await mountTicket(fake)

    await wrapper.get('[data-test="reject"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('try again in a moment')
    expect(wrapper.find('[data-test="reject"]').exists()).toBe(true)
    expect(wrapper.emitted('decided')).toBeUndefined()
  })

  it('shows the expired outcome when the server says the approval expired', async () => {
    const fake = server().on('POST', '/api/v1/approvals/a-1/approve', 409, { status: 409, code: 'approval.expired', detail: 'The approval expired.' })
    const { wrapper } = await mountTicket(fake)

    await wrapper.get('[data-test="approve"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-test="result"]').text()).toContain('Expired')
  })
})
