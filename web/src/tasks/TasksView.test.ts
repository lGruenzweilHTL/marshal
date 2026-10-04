import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import TasksView from './TasksView.vue'
import { FakeServer } from '../testing/fakeServer'
import { taskItem } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'

const tasks = [
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
  taskItem({ id: 't-3', name: 'backup', goal: 'Nightly backup.', status: 'done' }),
  taskItem({ id: 't-4', name: 'babysitter', goal: 'Watch the print.', status: 'running' }),
]

function server(active = false) {
  return new FakeServer()
    .on('GET', '/api/v1/me', 200, me)
    .antiforgery('xsrf')
    .on('GET', '/api/v1/tasks', 200, tasks)
    .on('GET', '/api/v1/tasks/kill-switch', 200, { active })
}

async function mountTasks(fake: FakeServer, path = '/tasks') {
  const mounted = await mountOptions(fake, path)
  const wrapper = mount(TasksView, { ...mounted.options, attachTo: document.body })
  await flushPromises()
  return { ...mounted, wrapper }
}

const names = (wrapper: { findAll(selector: string): { text(): string }[] }) => wrapper.findAll('[data-task] [data-test="name"]').map((cell) => cell.text())

describe('TasksView', () => {
  it('lists active tasks with status, trigger, next run and budget use on desktop', async () => {
    useViewport('desktop')
    const { wrapper } = await mountTasks(server())

    expect(names(wrapper)).toEqual(['briefing', 'Tell me when Alice is home.', 'babysitter'])
    const row = wrapper.get('[data-task="t-1"]')
    expect(row.text()).toContain('waiting')
    expect(row.text()).toContain('0 6 * * *')
    expect(row.text()).toMatch(/0\.12.*0\.50/)
    expect(row.get('a').attributes('href')).toBe('/tasks/t-1')
    expect(wrapper.get('[data-task="t-2"]').text()).toContain('person.alice')
    wrapper.unmount()
  })

  it('lists tasks as rows with links on phones', async () => {
    useViewport('phone')
    const { wrapper } = await mountTasks(server())

    expect(wrapper.find('table').exists()).toBe(false)
    expect(wrapper.get('a[href="/tasks/t-1"]').text()).toContain('briefing')
    wrapper.unmount()
  })

  it('filters by status with counts and keeps the filter in the query', async () => {
    useViewport('desktop')
    const { wrapper, router } = await mountTasks(server())

    expect(wrapper.get('[data-status="done"]').text()).toContain('1')
    await wrapper.get('[data-status="done"]').trigger('click')
    await flushPromises()

    expect(router.currentRoute.value.query).toEqual({ status: 'done' })
    expect(names(wrapper)).toEqual(['backup'])
    wrapper.unmount()
  })

  it('opens with the filter from the query', async () => {
    useViewport('desktop')
    const { wrapper } = await mountTasks(server(), '/tasks?status=all')

    expect(names(wrapper)).toHaveLength(4)
    wrapper.unmount()
  })

  it('searches name and goal', async () => {
    useViewport('desktop')
    const { wrapper } = await mountTasks(server())

    await wrapper.get('input[type="search"]').setValue('alice')

    expect(names(wrapper)).toEqual(['Tell me when Alice is home.'])
    wrapper.unmount()
  })

  it('reloads when a task changes', async () => {
    useViewport('desktop')
    const fake = server()
    const { live, wrapper } = await mountTasks(fake)

    live.publish('tasks', 'task.changed', { taskId: 't-1', name: 'briefing', status: 'running', statusReason: null, nextRunAt: null, resultAt: null })
    await flushPromises()

    expect(fake.count('GET', '/api/v1/tasks')).toBe(2)
    wrapper.unmount()
  })

  it('shows the kill switch while a task runs and stops after confirming', async () => {
    useViewport('desktop')
    const fake = server().on('POST', '/api/v1/tasks/kill-switch/activate', 200, { active: true, cancelled: 2 })
    const { wrapper } = await mountTasks(fake)

    await wrapper.get('[data-test="kill-switch"] [data-test="stop"]').trigger('click')
    ;(document.body.querySelector('[data-test="confirm-stop"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(fake.count('POST', '/api/v1/tasks/kill-switch/activate')).toBe(1)
    expect(wrapper.get('[data-test="kill-switch"]').text()).toContain('2 cancelled')
    wrapper.unmount()
  })

  it('shows the error with a retry when the tasks cannot be loaded', async () => {
    useViewport('desktop')
    const fake = server().on('GET', '/api/v1/tasks', 500, { status: 500, code: 'server.unexpected' })
    const { wrapper } = await mountTasks(fake)

    expect(wrapper.text()).toContain('Try again')
    wrapper.unmount()
  })
})
