import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import TemperatureChart from './TemperatureChart.vue'
import { FakeServer } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

describe('TemperatureChart', () => {
  it('draws a single reading as a flat line across the chart', async () => {
    const mounted = await mountOptions(new FakeServer().on('GET', '/api/v1/me', 200, me), '/')
    const chart = mount(TemperatureChart, { ...mounted.options, props: { samples: [{ extruder: 245, bed: 85 }] } })
    await flushPromises()
    expect(chart.get('path.extruder').attributes('d')).toMatch(/^M0,[\d.]+ L680,[\d.]+$/)
  })
})
