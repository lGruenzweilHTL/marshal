import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ConversationList from './ConversationList.vue'
import { FakeServer } from '../testing/fakeServer'
import { conversationSummary as conversation } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

const list = '/api/v1/conversations'

async function mountList(server: FakeServer, activeId: string | null = null) {
  const { options } = await mountOptions(server.on('GET', '/api/v1/me', 200, me), '/chat')
  const wrapper = mount(ConversationList, { ...options, props: { activeId } })
  await flushPromises()
  return wrapper
}

describe('ConversationList', () => {
  it('lists conversations in server order with voice and API marks', async () => {
    const server = new FakeServer().on('GET', list, 200, [
      conversation({ id: 'v1', source: 'voice', title: 'Set a timer', satelliteId: 'assist_satellite.kitchen', areaId: 'kitchen', canContinue: false }),
      conversation({ id: 'w1', title: 'Printer status' }),
      conversation({ id: 'a1', source: 'api', title: 'ping', canContinue: false }),
    ])
    const wrapper = await mountList(server, 'w1')

    const rows = wrapper.findAll('[data-conversation]')
    expect(rows.map((row) => row.attributes('data-conversation'))).toEqual(['v1', 'w1', 'a1'])
    expect(rows[0]!.attributes('href')).toBe('/chat/v1')
    expect(rows[0]!.text()).toContain('Set a timer')
    expect(rows[0]!.get('[data-test="mark"]').text()).toBe('kitchen')
    expect(rows[0]!.find('[data-test="voice"]').exists()).toBe(true)
    expect(rows[1]!.find('[data-test="mark"]').exists()).toBe(false)
    expect(rows[1]!.attributes('aria-current')).toBe('page')
    expect(rows[2]!.get('[data-test="mark"]').text()).toBe('API')
    expect(server.request('GET', list)!.search).toContain('limit=50')
  })

  it('falls back to the area when a voice conversation has no satellite', async () => {
    const server = new FakeServer().on('GET', list, 200, [conversation({ id: 'v1', source: 'voice', satelliteId: null, areaId: 'garage', canContinue: false })])
    const wrapper = await mountList(server)

    expect(wrapper.get('[data-test="mark"]').text()).toBe('garage')
  })

  it('shows the empty state', async () => {
    const wrapper = await mountList(new FakeServer().on('GET', list, 200, []))

    expect(wrapper.text()).toContain('No conversations yet')
  })

  it('shows the error state with a retry', async () => {
    const server = new FakeServer().on('GET', list, 500, { code: 'server.unexpected', detail: 'Boom.' })
    const wrapper = await mountList(server)

    expect(wrapper.text()).toContain('The conversations could not be loaded')
    server.on('GET', list, 200, [conversation({ id: 'w1' })])
    await wrapper.get('[data-test="retry"]').trigger('click')
    await flushPromises()
    expect(wrapper.findAll('[data-conversation]')).toHaveLength(1)
  })
})
