import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import CostView from './CostView.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

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

describe('CostView', () => {
  it('shows cost per day and tier with the advisor on its own line', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/cost', 200, report)
    const { options } = await mountOptions(fake, '/cost')
    const wrapper = mount(CostView, options)
    await flushPromises()

    const headers = wrapper.findAll('thead th').map((cell) => cell.text())
    expect(headers).toEqual(['Day', 'fast', 'standard', 'Advisor', 'Total'])
    const today = wrapper.get('[data-day="2026-09-28"]').findAll('td').map((cell) => cell.text())
    expect(today).toEqual(['2026-09-28', '0.25', '0.50', '1.00', '1.75'])
    const totals = wrapper.get('tfoot tr').findAll('td').map((cell) => cell.text())
    expect(totals).toEqual(['Total', '0.75', '0.50', '1.00', '2.25'])
    expect(wrapper.text()).toContain('Europe/Vienna')
  })

  it('requests the chosen range', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/cost', 200, report)
    const { options } = await mountOptions(fake, '/cost')
    const wrapper = mount(CostView, options)
    await flushPromises()

    await wrapper.get('input[name="from"]').setValue('2026-09-01')
    await wrapper.get('input[name="to"]').setValue('2026-09-15')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(fake.request('GET', '/api/v1/cost')!.search).toBe('?from=2026-09-01&to=2026-09-15')
  })
})
