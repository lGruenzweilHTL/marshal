import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import TasksView from './TasksView.vue'
import { FakeServer } from '../testing/fakeServer'
import { taskItem } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server(active = false) {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .antiforgery('xsrf')
    .on('GET', '/api/v1/tasks', 200, [
      taskItem(),
      taskItem({
        id: 't-2',
        name: null,
        goal: 'Tell me when Alice is home.',
        status: 'pending',
        trigger: { kind: 'event', spec: '{"source":"ha","entity":"person.alice","to":"home"}' },
        nextRunAt: null,
        budget: { tokens: null, cost: null, steps: null, wallTimeSeconds: null, runs: null },
        resultSpeech: null,
        resultAt: null,
      }),
    ])
    .on('GET', '/api/v1/tasks/kill-switch', 200, { active })
}

async function mountTasks(fake: FakeServer, path = '/tasks') {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(TasksView, mounted.options)
  await flushPromises()
  return { ...mounted, wrapper }
}

describe('TasksView', () => {
  it('lists tasks and watches with status, trigger, next run, budget use and latest result', async () => {
    const { wrapper } = await mountTasks(server())

    const rows = wrapper.findAll('tbody tr')
    expect(rows).toHaveLength(2)
    expect(rows[0]!.text()).toContain('briefing')
    expect(rows[0]!.text()).toContain('waiting')
    expect(rows[0]!.text()).toContain('cron 0 6 * * *')
    expect(rows[0]!.text()).toMatch(/0\.12.*0\.50/)
    expect(rows[0]!.text()).toContain('Two new mails.')
    expect(rows[0]!.get('a').attributes('href')).toBe('/tasks/t-1')
    expect(rows[1]!.text()).toContain('watch')
    expect(rows[1]!.text()).toContain('Tell me when Alice is home.')
  })

  it('filters by status through the query', async () => {
    const fake = server()
    const { wrapper, router } = await mountTasks(fake)

    await wrapper.get('select[name="status"]').setValue('done')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ status: 'done' })
    expect(new URLSearchParams(fake.request('GET', '/api/v1/tasks')!.search).get('status')).toBe('done')
  })

  it('shows active tasks by default', async () => {
    const fake = server()
    await mountTasks(fake)

    expect(new URLSearchParams(fake.request('GET', '/api/v1/tasks')!.search).get('status')).toBe('active')
  })

  it('reloads when a task changes', async () => {
    const fake = server()
    const { live } = await mountTasks(fake)

    live.publish('tasks', 'task.changed', { taskId: 't-1', name: 'briefing', status: 'running', statusReason: null, nextRunAt: null, resultAt: null })
    await flushPromises()

    expect(fake.count('GET', '/api/v1/tasks')).toBe(2)
  })

  it('stops all background work only after confirming, and shows how many tasks were cancelled', async () => {
    const fake = server().on('POST', '/api/v1/tasks/kill-switch/activate', 200, { active: true, cancelled: 2 })
    const { wrapper } = await mountTasks(fake)

    await wrapper.get('[data-test="kill-switch"] button').trigger('click')
    expect(fake.count('POST', '/api/v1/tasks/kill-switch/activate')).toBe(0)
    await wrapper.get('[data-test="kill-switch-confirm"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/tasks/kill-switch/activate')).toBe(1)
    expect(fake.request('POST', '/api/v1/tasks/kill-switch/activate')!.headers.get('X-XSRF-TOKEN')).toBe('xsrf')
    expect(wrapper.get('[data-test="kill-switch"]').text()).toContain('stopped')
    expect(wrapper.get('[data-test="kill-switch"]').text()).toContain('2 tasks cancelled')
  })

  it('resumes background work when the kill switch is active', async () => {
    const fake = server(true).on('POST', '/api/v1/tasks/kill-switch/release', 200, { active: false })
    const { wrapper } = await mountTasks(fake)

    expect(wrapper.get('[data-test="kill-switch"]').text()).toContain('stopped')
    await wrapper.get('[data-test="kill-switch-release"]').trigger('click')
    await flushPromises()

    expect(fake.count('POST', '/api/v1/tasks/kill-switch/release')).toBe(1)
    expect(wrapper.get('[data-test="kill-switch"]').text()).toContain('running normally')
  })
})
