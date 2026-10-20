import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import RunsView from './RunsView.vue'
import { FakeServer, json } from '../testing/fakeServer'
import { runSummary } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

function server() {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .on('GET', '/api/v1/runs', 200, [
      runSummary({ id: 'r-2', source: 'web', trigger: 'conversation', cost: 0.25, tainted: true }),
      runSummary({ id: 'r-1', outcome: 'error' }),
    ])
}

async function mountRuns(fake: FakeServer, path = '/runs') {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(RunsView, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('RunsView', () => {
  beforeEach(() => useViewport('desktop'))

  it('lists runs with source, outcome, cost and taint, linking to the detail', async () => {
    const { wrapper } = await mountRuns(server())

    const rows = wrapper.findAll('[data-run]')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('Web')
    expect(rows[0]!.text()).toContain('0.25')
    expect(rows[0]!.text()).toContain('tainted')
    expect(rows[1]!.text()).toContain('error')
    expect(rows[0]!.get('a').attributes('href')).toBe('/runs/r-2')
  })

  it('shows the time to first words and sorts slowest first through the query', async () => {
    const fake = new FakeServer()
      .on('GET', '/api/v1/me', 200, me)
      .on('GET', '/api/v1/runs', 200, [runSummary({ id: 'r-2', timeToFirstDeltaMs: 1600 }), runSummary({ id: 'r-1', source: 'web', timeToFirstDeltaMs: null })])
    const { wrapper, router } = await mountRuns(fake)

    expect(wrapper.get('[data-run="r-2"]').text()).toContain('1.6 s')
    expect(wrapper.get('[data-run="r-1"]').text()).toContain('–')

    await wrapper.get('[data-sort="timeToFirstDelta"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ sort: 'timeToFirstDelta' })
    expect(Object.fromEntries(new URLSearchParams(fake.request('GET', '/api/v1/runs')!.search))).toEqual({ sort: 'timeToFirstDelta', limit: '50' })
    expect(wrapper.find('[data-test="more"]').exists()).toBe(false)
  })

  it('filters by source, outcome and minimum cost through the query', async () => {
    const fake = server()
    const { wrapper, router } = await mountRuns(fake)

    await wrapper.get('[data-source="voice"]').trigger('click')
    await flushPromises()
    await wrapper.get('[data-outcome="error"]').trigger('click')
    await flushPromises()
    await wrapper.get('input[name="minCost"]').setValue('0.1')
    await wrapper.get('input[name="minCost"]').trigger('change')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ source: 'voice', outcome: 'error', minCost: '0.1' })
    expect(Object.fromEntries(new URLSearchParams(fake.request('GET', '/api/v1/runs')!.search))).toEqual(
      { source: 'voice', outcome: 'error', minCost: '0.1', limit: '50' },
    )
  })

  it('filters by rating through the query', async () => {
    const fake = server()
    const { wrapper, router } = await mountRuns(fake)

    await wrapper.get('[data-rating="down"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ rating: 'down' })
    expect(Object.fromEntries(new URLSearchParams(fake.request('GET', '/api/v1/runs')!.search))).toEqual({ rating: 'down', limit: '50' })
  })

  it('filters automation runs and labels them', async () => {
    const fake = new FakeServer()
      .on('GET', '/api/v1/me', 200, me)
      .on('GET', '/api/v1/runs', 200, [runSummary({ id: 'r-9', source: 'automation', trigger: 'ai_task:doorbell' })])
    const { wrapper, router } = await mountRuns(fake)

    expect(wrapper.text()).toContain('Automation')
    await wrapper.get('[data-source="automation"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ source: 'automation' })
    expect(new URLSearchParams(fake.request('GET', '/api/v1/runs')!.search).get('source')).toBe('automation')
  })

  it('loads older runs before the last one shown when the page was full', async () => {
    const page = Array.from({ length: 50 }, (_, index) => runSummary({ id: `r-${index}`, startedAt: `2026-09-28T08:${String(59 - index).padStart(2, '0')}:00Z` }))
    const fake = new FakeServer()
      .on('GET', '/api/v1/me', 200, me)
      .handle('GET', '/api/v1/runs', (request) =>
        json(200, new URLSearchParams(request.search).get('before') ? [runSummary({ id: 'r-old', startedAt: '2026-09-27T08:00:00Z' })] : page),
      )
    const { wrapper } = await mountRuns(fake)

    await wrapper.get('[data-test="more"]').trigger('click')
    await flushPromises()

    expect(new URLSearchParams(fake.request('GET', '/api/v1/runs')!.search).get('before')).toBe('2026-09-28T08:10:00Z')
    expect(wrapper.findAll('[data-run]')).toHaveLength(51)
    expect(wrapper.find('[data-test="more"]').exists()).toBe(false)
  })

  it('lists runs as rows on phones', async () => {
    useViewport('phone')
    const { wrapper } = await mountRuns(server())

    expect(wrapper.find('table').exists()).toBe(false)
    expect(wrapper.get('a[href="/runs/r-2"]').text()).toContain('Web')
  })

  it('shows the error with a retry when the runs cannot be loaded', async () => {
    const { wrapper } = await mountRuns(server().on('GET', '/api/v1/runs', 503, { status: 503 }))

    expect(wrapper.text()).toContain('Marshal is not available right now.')
    expect(wrapper.text()).toContain('Try again')
  })

  it('offers no older runs after a short page', async () => {
    const { wrapper } = await mountRuns(server())

    expect(wrapper.find('[data-test="more"]').exists()).toBe(false)
  })

  it('reloads when a run completes', async () => {
    const fake = server()
    const { live } = await mountRuns(fake)

    live.publish('runs', 'run.completed', { runId: 'r-3', outcome: 'ok', cost: 0, endedAt: '2026-09-28T09:00:00Z' })
    await flushPromises()

    expect(fake.count('GET', '/api/v1/runs')).toBe(2)
  })
})
