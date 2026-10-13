import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ReasoningBlock from './ReasoningBlock.vue'
import { createAppI18n } from '../i18n'

function mountBlock(props: { text: string; open?: boolean; streaming?: boolean; withheld?: boolean }) {
  return mount(ReasoningBlock, { props, global: { plugins: [createAppI18n()] } })
}

describe('ReasoningBlock', () => {
  it('is open while reasoning streams and says so', () => {
    const wrapper = mountBlock({ text: 'Check the printer first.', open: true, streaming: true })

    expect(wrapper.get('details').attributes('open')).toBeDefined()
    expect(wrapper.get('summary').text()).toBe('Thinking…')
    expect(wrapper.get('[data-test="reasoning-text"]').text()).toBe('Check the printer first.')
  })

  it('is collapsed once closed and labels the finished reasoning', () => {
    const wrapper = mountBlock({ text: 'Check the printer first.', open: false })

    expect(wrapper.get('details').attributes('open')).toBeUndefined()
    expect(wrapper.get('summary').text()).toBe('Reasoning')
  })

  it('reports toggles so the user can open and close it', async () => {
    const wrapper = mountBlock({ text: 'Check the printer first.', open: false })
    const details = wrapper.get('details').element as HTMLDetailsElement

    details.open = true
    await wrapper.get('details').trigger('toggle')
    details.open = false
    await wrapper.get('details').trigger('toggle')

    expect(wrapper.emitted('update:open')).toEqual([[true], [false]])
  })

  it('shows markup as literal text and loads no image', () => {
    const markup = '![x](https://evil.example/a.png) <img src="https://evil.example/b.png"> **bold**'
    const wrapper = mountBlock({ text: markup, open: true })

    expect(wrapper.find('img').exists()).toBe(false)
    expect(wrapper.find('strong').exists()).toBe(false)
    expect(wrapper.get('[data-test="reasoning-text"]').text()).toBe(markup)
  })

  it('notes reasoning that was not kept', () => {
    const wrapper = mountBlock({ text: '', withheld: true })

    expect(wrapper.find('[data-test="reasoning-text"]').exists()).toBe(false)
    expect(wrapper.get('[data-test="reasoning-withheld"]').text()).toContain('not kept')
  })
})
