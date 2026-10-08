import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import TriggerLabel from './TriggerLabel.vue'
import { FakeServer } from '../testing/fakeServer'
import { mountOptions } from '../testing/mountApp'

const watch = { kind: 'event' as const, spec: '{"source":"ha","entity":"person.alice","to":"Hornbach"}' }

async function mountLabel(trigger: typeof watch & { once: boolean }, language: 'en' | 'de' = 'en') {
  const mounted = await mountOptions(new FakeServer(), '/tasks', { language })
  return mount(TriggerLabel, { ...mounted.options, props: { trigger } })
}

describe('TriggerLabel', () => {
  it('marks a once-watch in English', async () => {
    const wrapper = await mountLabel({ ...watch, once: true })
    expect(wrapper.find('.once').text()).toBe('once')
  })

  it('marks a once-watch in German', async () => {
    const wrapper = await mountLabel({ ...watch, once: true }, 'de')
    expect(wrapper.find('.once').text()).toBe('einmalig')
  })

  it('shows no marker for a recurring watch', async () => {
    const wrapper = await mountLabel({ ...watch, once: false })
    expect(wrapper.find('.once').exists()).toBe(false)
  })
})
