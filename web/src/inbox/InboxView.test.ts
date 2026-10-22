import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import InboxView from './InboxView.vue'
import { FakeServer, json } from '../testing/fakeServer'
import { approvalItem, notificationEntry, proposal, skillProposalSummary, suggestionItem } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

const future = new Date(Date.now() + 5 * 60_000).toISOString()

function server() {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .antiforgery('xsrf')
    .on('GET', '/api/v1/approvals', 200, [
      approvalItem({ expiresAt: future }),
      approvalItem({ id: 'a-2', summary: 'Start the print', tool: 'printer.start_print', taint: [], expiresAt: future }),
    ])
    .on('GET', '/api/v1/approvals/inputs', 200, [])
    .on('GET', '/api/v1/proposals', 200, [proposal()])
    .on('GET', '/api/v1/skill-proposals', 200, [])
    .on('GET', '/api/v1/suggestions', 200, [])
    .on('GET', '/api/v1/notifications', 200, [
      notificationEntry(),
      notificationEntry({ id: 'n-2', priority: 'critical', content: 'Print failure suspected.', outcome: 'held' }),
    ])
}

async function mountInbox(fake: FakeServer, path = '/inbox') {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(InboxView, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('InboxView', () => {
  it('lists approvals before proposals before notifications, with counts on the filter', async () => {
    useViewport('phone')
    const { wrapper } = await mountInbox(server())

    const sections = wrapper.findAll('[data-section]').map((section) => section.attributes('data-section'))
    expect(sections).toEqual(['approvals', 'proposals', 'notifications'])
    expect(wrapper.get('[data-test="filter"]').text()).toContain('Approvals 2')
    expect(wrapper.findAll('[data-approval]')).toHaveLength(2)
    expect(wrapper.get('a[href="/inbox/proposals/p-1"]').text()).toContain(proposal().title)
    expect(wrapper.get('a[href="/inbox/notifications/n-2"]').text()).toContain('Print failure suspected.')
  })

  it('shows one kind of item per filter', async () => {
    useViewport('phone')
    const { wrapper } = await mountInbox(server())

    await wrapper.get('[data-test="filter"] [data-value="alerts"]').trigger('click')

    expect(wrapper.findAll('[data-section]').map((section) => section.attributes('data-section'))).toEqual(['notifications'])
  })

  it('filters notifications by outcome on the server', async () => {
    useViewport('phone')
    const fake = server()
    fake.handle('GET', '/api/v1/notifications', (request) =>
      json(200, request.search.includes('outcome=held') ? [notificationEntry({ id: 'n-2', outcome: 'held' })] : [notificationEntry()]),
    )
    const { wrapper } = await mountInbox(fake)

    await wrapper.get('[data-test="filter"] [data-value="alerts"]').trigger('click')
    await wrapper.get('[data-outcome="held"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('a[href="/inbox/notifications/n-1"]').exists()).toBe(false)
    expect(wrapper.find('a[href="/inbox/notifications/n-2"]').exists()).toBe(true)
  })

  it('loads older notifications page by page', async () => {
    useViewport('phone')
    const fake = server()
    const page = Array.from({ length: 50 }, (_, index) => notificationEntry({ id: `n-${index}`, createdAt: new Date(Date.UTC(2026, 8, 30, 8, 0) - index * 60_000).toISOString() }))
    fake.handle('GET', '/api/v1/notifications', (request) => json(200, request.search.includes('before=') ? [notificationEntry({ id: 'n-old' })] : page))
    const { wrapper } = await mountInbox(fake)

    await wrapper.get('[data-test="more"]').trigger('click')
    await flushPromises()

    expect(fake.requests.findLast((request) => request.path === '/api/v1/notifications')?.search).toContain('before=')
    expect(wrapper.find('a[href="/inbox/notifications/n-old"]').exists()).toBe(true)
    expect(wrapper.find('[data-test="more"]').exists()).toBe(false)
  })

  it('on desktop shows the selected approval in full next to the list', async () => {
    useViewport('desktop')
    const { wrapper } = await mountInbox(server())

    const detail = wrapper.get('[data-test="detail"]')
    expect(detail.get('[data-approval]').attributes('data-approval')).toBe('a-1')
    expect(detail.text()).toContain('"door": "front"')

    await wrapper.get('[data-select="a-2"]').trigger('click')
    expect(wrapper.get('[data-test="detail"] [data-approval]').attributes('data-approval')).toBe('a-2')
  })

  it('reloads after a decision and confirms it with a toast', async () => {
    useViewport('phone')
    const fake = server().on('POST', '/api/v1/approvals/a-1/approve', 200, { id: 'a-1', status: 'approved' })
    const { wrapper, toasts } = await mountInbox(fake)

    await wrapper.get('[data-approval="a-1"] [data-test="approve"]').trigger('click')
    await flushPromises()

    expect(fake.count('GET', '/api/v1/approvals')).toBe(2)
    expect(toasts.items.value.map((toast) => toast.message)).toContain('Approved. The action ran and the task continues.')
  })

  it('keeps the parts that loaded when one list fails', async () => {
    useViewport('phone')
    const fake = server().on('GET', '/api/v1/proposals', 500, { status: 500, code: 'server.unexpected', title: 'server.unexpected' })
    const { wrapper } = await mountInbox(fake)

    expect(wrapper.findAll('[data-approval]')).toHaveLength(2)
    expect(wrapper.get('[data-section="proposals"]').text()).toContain('Try again')
  })

  it('shows the empty state when nothing needs the user', async () => {
    useViewport('phone')
    const fake = new FakeServer()
      .on('GET', '/api/v1/me', 200, me)
      .on('GET', '/api/v1/approvals', 200, [])
      .on('GET', '/api/v1/approvals/inputs', 200, [])
      .on('GET', '/api/v1/proposals', 200, [])
      .on('GET', '/api/v1/skill-proposals', 200, [])
      .on('GET', '/api/v1/suggestions', 200, [])
      .on('GET', '/api/v1/notifications', 200, [])
    const { wrapper } = await mountInbox(fake)

    expect(wrapper.text()).toContain('Nothing needs you')
  })

  it('lists input requests with their forms next to the approvals', async () => {
    useViewport('phone')
    const fake = server().on('GET', '/api/v1/approvals/inputs', 200, [
      {
        id: 'i-1',
        serverId: 'paperless',
        tool: 'paperless.upload',
        mode: 'url',
        message: 'Log in to Paperless.',
        requestedSchema: null,
        url: 'https://paperless.example/login',
        sessionId: null,
        taskId: 't-1',
        status: 'pending',
        createdAt: '2026-10-16T08:00:00Z',
        expiresAt: future,
      },
    ])
    const { wrapper } = await mountInbox(fake)

    const sections = wrapper.findAll('[data-section]').map((section) => section.attributes('data-section'))
    expect(sections.slice(0, 2)).toEqual(['approvals', 'inputs'])
    expect(wrapper.get('[data-input="i-1"]').text()).toContain('Log in to Paperless.')
    expect(wrapper.get('[data-test="filter"]').text()).toContain('Approvals 3')
  })

  it('lists skill proposals after change proposals and suggestions before notifications', async () => {
    useViewport('phone')
    const fake = server()
      .on('GET', '/api/v1/skill-proposals', 200, [skillProposalSummary(), skillProposalSummary({ id: 'sp-2', name: 'old-one', status: 'downloaded' })])
      .on('GET', '/api/v1/suggestions', 200, [suggestionItem()])
    const { wrapper } = await mountInbox(fake)

    const sections = wrapper.findAll('[data-section]').map((section) => section.attributes('data-section'))
    expect(sections).toEqual(['approvals', 'proposals', 'skills', 'suggestions', 'notifications'])
    expect(wrapper.get('a[href="/inbox/skills/sp-1"]').text()).toContain('nightly-backup-check')
    expect(wrapper.get('a[href="/inbox/skills/sp-2"]').text()).toContain('Downloaded')
    expect(wrapper.get('[data-suggestion="sg-1"]').text()).toContain('The bedroom window is open')
    expect(wrapper.get('[data-suggestion="sg-1"]').text()).toContain('Close cover.bedroom_window')
  })

  it('accepts a suggestion, removes it and shows what the action returned', async () => {
    useViewport('phone')
    const fake = server()
      .on('GET', '/api/v1/suggestions', 200, [suggestionItem()])
      .on('POST', '/api/v1/suggestions/sg-1/accept', 200, { suggestion: suggestionItem({ status: 'accepted' }), actionResult: 'Closed.' })
    const { wrapper, toasts } = await mountInbox(fake)

    fake.on('GET', '/api/v1/suggestions', 200, [])
    await wrapper.get('[data-suggestion="sg-1"] [data-test="accept"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/suggestions/sg-1/accept')).toBe(1)
    expect(wrapper.find('[data-suggestion="sg-1"]').exists()).toBe(false)
    expect(toasts.items.value.map((toast) => toast.message)).toContain('Accepted. Closed.')
  })

  it('dismisses a suggestion without running anything', async () => {
    useViewport('phone')
    const fake = server()
      .on('GET', '/api/v1/suggestions', 200, [suggestionItem()])
      .on('POST', '/api/v1/suggestions/sg-1/dismiss', 200, { suggestion: suggestionItem({ status: 'dismissed' }), actionResult: null })
    const { wrapper, toasts } = await mountInbox(fake)

    fake.on('GET', '/api/v1/suggestions', 200, [])
    await wrapper.get('[data-suggestion="sg-1"] [data-test="dismiss"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/suggestions/sg-1/accept')).toBe(0)
    expect(fake.count('POST', '/api/v1/suggestions/sg-1/dismiss')).toBe(1)
    expect(toasts.items.value.map((toast) => toast.message)).toContain('Dismissed. Marshal suggests this kind of thing less often.')
  })

  it('shows why accepting confirms an action, and a suggested snippet verbatim', async () => {
    useViewport('phone')
    const fake = server().on('GET', '/api/v1/suggestions', 200, [
      suggestionItem({ actionSummary: 'Turn off switch.voron_power', actionReasons: ['Switching off a printer is confirm tier.'] }),
      suggestionItem({ id: 'sg-2', detector: 'maintenance_counter', actionSummary: null, snippet: 'maintenance:\n  items:\n' }),
    ])
    const { wrapper } = await mountInbox(fake)

    expect(wrapper.get('[data-suggestion="sg-1"] [data-test="reasons"]').text()).toContain('Switching off a printer is confirm tier.')
    expect(wrapper.get('[data-suggestion="sg-2"] pre').text()).toBe('maintenance:\n  items:')
    expect(wrapper.find('[data-suggestion="sg-2"] [data-test="reasons"]').exists()).toBe(false)
  })
})
