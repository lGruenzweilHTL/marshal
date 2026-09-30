import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import NotificationsView from './NotificationsView.vue'
import { FakeServer, json } from '../testing/fakeServer'
import { notificationEntry } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server() {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .on('GET', '/api/v1/notifications', 200, [
      notificationEntry({ id: 'n-2', content: 'Parcel arrived.', outcome: 'held', channel: null, reason: 'quiet_hours', priority: 'low' }),
      notificationEntry(),
    ])
}

async function mountHistory(fake: FakeServer, path = '/notifications') {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(NotificationsView, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('NotificationsView', () => {
  it('lists notifications with content, priority, channel, outcome and reason', async () => {
    const { wrapper } = await mountHistory(server())

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Parcel arrived.')
    expect(rows[0]!.text()).toContain('low')
    expect(rows[0]!.text()).toContain('held')
    expect(rows[0]!.text()).toContain('quiet_hours')
    expect(rows[1]!.text()).toContain('voice')
    expect(rows[1]!.text()).toContain('delivered')
  })

  it('filters by outcome through the query', async () => {
    const fake = server()
    const { wrapper, router } = await mountHistory(fake)

    await wrapper.get('select[name="outcome"]').setValue('held')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ outcome: 'held' })
    expect(new URLSearchParams(fake.request('GET', '/api/v1/notifications')!.search).get('outcome')).toBe('held')
  })

  it('loads older notifications before the last one shown when the page was full', async () => {
    const page = Array.from({ length: 50 }, (_, index) => notificationEntry({ id: `n-${index}`, createdAt: `2026-09-30T08:${String(59 - index).padStart(2, '0')}:00Z` }))
    const fake = new FakeServer()
      .on('GET', '/api/v1/me', 200, me)
      .handle('GET', '/api/v1/notifications', (request) => json(200, new URLSearchParams(request.search).get('before') ? [notificationEntry({ id: 'n-old' })] : page))
    const { wrapper } = await mountHistory(fake)

    await wrapper.get('[data-test="more"]').trigger('click')
    await flushPromises()

    expect(new URLSearchParams(fake.request('GET', '/api/v1/notifications')!.search).get('before')).toBe('2026-09-30T08:10:00Z')
    expect(wrapper.findAll('tbody tr')).toHaveLength(51)
  })

  it('reloads when a notification is created or updated', async () => {
    const fake = server()
    const { live } = await mountHistory(fake)
    const payload = { id: 'n-3', source: 'task:t-1', priority: 'normal' as const, text: 'Hi.', outcome: 'pending' as const, channel: null, reason: null, taskId: null, createdAt: '2026-09-30T09:00:00Z' }

    live.publish('notifications', 'notification.created', payload)
    live.publish('notifications', 'notification.updated', { ...payload, outcome: 'delivered' })
    await flushPromises()

    expect(fake.count('GET', '/api/v1/notifications')).toBe(3)
  })
})
