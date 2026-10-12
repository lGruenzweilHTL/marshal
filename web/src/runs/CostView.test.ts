import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CostView from './CostView.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

const report = {
  timeZone: 'Europe/Vienna',
  from: '2026-09-27',
  to: '2026-09-28',
  days: [
    { date: '2026-09-27', total: 0.5, byTier: { fast: 0.5 }, advisor: 0 },
    { date: '2026-09-28', total: 1.75, byTier: { fast: 0.25, standard: 0.5 }, advisor: 1 },
  ],
  total: 2.25,
  byTier: { fast: 0.75, standard: 0.5 },
  advisor: 1,
}

async function mountCost(options: { language?: 'en' | 'de' } = {}, fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/cost', 200, report)) {
  const mounted = await mountOptions(fake, '/cost', options)
  const wrapper = mount(CostView, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper, fake }
}

describe('CostView', () => {
  beforeEach(() => {
    useViewport('desktop')
    vi.useFakeTimers({ toFake: ['Date'] })
    vi.setSystemTime(new Date('2026-09-28T10:00:00Z'))
  })
  afterEach(() => vi.useRealTimers())

  it('shows cost per day and tier with the advisor on its own line', async () => {
    const { wrapper } = await mountCost()

    const headers = wrapper.findAll('thead th').map((cell) => cell.text())
    expect(headers).toEqual(['Day', 'Fast', 'Standard', 'Advisor', 'Total'])
    const today = wrapper.get('[data-day="2026-09-28"]').findAll('td').map((cell) => cell.text())
    expect(today).toEqual(['Sep 28, 2026', '0.25', '0.50', '1.00', '1.75'])
    const totals = wrapper.get('tfoot tr').findAll('td').map((cell) => cell.text())
    expect(totals).toEqual(['Total', '0.75', '0.50', '1.00', '2.25'])
    expect(wrapper.text()).toContain('Europe/Vienna')
  })

  it('sums up today, the range and the advisor share', async () => {
    const { wrapper } = await mountCost()

    const summary = wrapper.get('[data-test="summary"]').text()
    expect(summary).toContain('1.75')
    expect(summary).toContain('2.25')
    expect(summary).toContain('44.4%')
  })

  it('formats numbers and dates in German', async () => {
    const { wrapper } = await mountCost({ language: 'de' })

    const today = wrapper.get('[data-day="2026-09-28"]').findAll('td').map((cell) => cell.text())
    expect(today).toEqual(['28.09.2026', '0,25', '0,50', '1,00', '1,75'])
    expect(wrapper.get('thead th').text()).toBe('Tag')
  })

  it('requests the range of a preset', async () => {
    const { wrapper, fake } = await mountCost()

    await wrapper.get('[data-value="30"]').trigger('click')
    await flushPromises()

    expect(fake.request('GET', '/api/v1/cost')!.search).toBe('?from=2026-08-30&to=2026-09-28')
  })

  it('requests a custom range', async () => {
    const { wrapper, fake } = await mountCost()

    await wrapper.get('input[name="from"]').setValue('2026-09-01')
    await wrapper.get('input[name="to"]').setValue('2026-09-15')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(fake.request('GET', '/api/v1/cost')!.search).toBe('?from=2026-09-01&to=2026-09-15')
  })

  it('lists days as rows on phones', async () => {
    useViewport('phone')
    const { wrapper } = await mountCost()

    expect(wrapper.find('table').exists()).toBe(false)
    expect(wrapper.get('[data-day="2026-09-28"]').text()).toContain('1.75')
  })
})
