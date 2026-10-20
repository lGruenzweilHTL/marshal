import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, describe, expect, it } from 'vitest'
import TaskDetailView from './TaskDetailView.vue'
import { FakeServer, json } from '../testing/fakeServer'
import { taskDetail, taskItem } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'

function server(detail = taskDetail()) {
  return new FakeServer().on('GET', '/api/v1/me', 200, me).antiforgery('xsrf').on('GET', '/api/v1/tasks/t-1', 200, detail)
}

async function mountDetail(fake: FakeServer) {
  const mounted = await mountOptions(fake, '/tasks/t-1')
  const wrapper = mount(TaskDetailView, { ...mounted.options, attachTo: document.body })
  await flushPromises()
  return { ...mounted, wrapper }
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('TaskDetailView', () => {
  it('shows goal, notes, the result body as markdown, deliveries, runs and notifications', async () => {
    const { wrapper } = await mountDetail(server())

    expect(wrapper.get('[data-test="goal"]').text()).toBe('Morning briefing.')
    expect(wrapper.get('[data-test="notes"]').text()).toBe('Mention the parcel.')
    expect(wrapper.get('[data-test="result"] strong').text()).toBe('Two')
    expect(wrapper.get('[data-test="result"] h2').text()).toBe('Mail')
    const delivery = wrapper.get('[data-test="deliveries"]').text()
    expect(delivery).toContain('voice')
    expect(delivery).toContain('state(binary_sensor.kitchen_occupancy = on)')
    expect(delivery).toContain('11:00')
    expect(wrapper.get('[data-test="runs"] a').attributes('href')).toBe('/runs/r-9')
    expect(wrapper.get('[data-test="facts"]').text()).toContain('0 6 * * *')
    expect(wrapper.get('[data-test="notifications"]').text()).toContain('delivered')
  })

  it('does not render HTML in the result body', async () => {
    const { wrapper } = await mountDetail(server(taskDetail({ result: { speech: 'x', body: '<img src=x onerror=alert(1)>' } })))

    expect(wrapper.find('[data-test="result"] img').exists()).toBe(false)
  })

  it('shows executor sessions with their status', async () => {
    const { wrapper } = await mountDetail(
      server(taskDetail({ executorSessions: [{ id: 's-1', taskId: 't-1', executor: 'claude_code', target: 'demo', status: 'running', startedAt: '2026-09-30T08:00:00Z', finishedAt: null }] })),
    )

    const sessions = wrapper.get('[data-test="executor-sessions"]').text()
    expect(sessions).toContain('claude_code')
    expect(sessions).toContain('demo')
    expect(sessions).toContain('running')
  })

  it('edits the goal', async () => {
    const fake = server().on('PUT', '/api/v1/tasks/t-1/goal', 200, taskItem({ goal: 'Morning briefing with the weather.' }))
    const { wrapper } = await mountDetail(fake)

    await wrapper.get('[data-test="edit-goal"]').trigger('click')
    await wrapper.get('textarea[name="goal"]').setValue('Morning briefing with the weather.')
    await wrapper.get('form[data-test="goal-form"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('PUT', '/api/v1/tasks/t-1/goal')!.body)).toEqual({ goal: 'Morning briefing with the weather.' })
    expect(wrapper.get('[data-test="goal"]').text()).toBe('Morning briefing with the weather.')
  })

  it('shows why a goal could not be saved', async () => {
    const fake = server().on('PUT', '/api/v1/tasks/t-1/goal', 400, { title: 'Bad request', detail: 'A task needs a goal.' })
    const { wrapper } = await mountDetail(fake)

    await wrapper.get('[data-test="edit-goal"]').trigger('click')
    await wrapper.get('textarea[name="goal"]').setValue('x')
    await wrapper.get('form[data-test="goal-form"]').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('A task needs a goal.')
    expect((wrapper.get('textarea[name="goal"]').element as HTMLTextAreaElement).value).toBe('x')
  })

  it('cancels an unfinished task after confirming', async () => {
    let cancelled = false
    const fake = server()
      .handle('GET', '/api/v1/tasks/t-1', () => json(200, taskDetail({ task: taskItem({ status: cancelled ? 'cancelled' : 'waiting' }) })))
      .handle('POST', '/api/v1/tasks/t-1/cancel', () => {
        cancelled = true
        return json(200, { cancelled: 1 })
      })
    const { wrapper } = await mountDetail(fake)

    await wrapper.get('[data-test="cancel"]').trigger('click')
    expect(fake.count('POST', '/api/v1/tasks/t-1/cancel')).toBe(0)
    ;(document.body.querySelector('[data-test="cancel-confirm"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(fake.count('POST', '/api/v1/tasks/t-1/cancel')).toBe(1)
    expect(wrapper.get('[data-test="status"]').text()).toContain('cancelled')
    expect(wrapper.find('[data-test="cancel"]').exists()).toBe(false)
  })

  it('offers no cancel for a finished task', async () => {
    const { wrapper } = await mountDetail(server(taskDetail({ task: taskItem({ status: 'done' }) })))

    expect(wrapper.find('[data-test="cancel"]').exists()).toBe(false)
  })

  it('says a task without a policy may use every tool its template allows', async () => {
    const { wrapper } = await mountDetail(server())

    expect(wrapper.get('[data-test="tool-policy"]').text()).toContain('Every tool its template allows')
  })

  it('shows the tool policy globs', async () => {
    const { wrapper } = await mountDetail(server(taskDetail({ toolPolicy: ['mail.*', 'agent.extract'] })))

    const policy = wrapper.get('[data-test="tool-policy"]').text()
    expect(policy).toContain('mail.*')
    expect(policy).toContain('agent.extract')
  })

  it('edits the tool policy by namespace and refreshes the detail', async () => {
    let saved: string[] | null = ['mail.*', 'agent.extract']
    const fake = server()
      .handle('GET', '/api/v1/tasks/t-1', () => json(200, taskDetail({ toolPolicy: saved })))
      .handle('PUT', '/api/v1/tasks/t-1/tool-policy', (request) => {
        saved = JSON.parse(request.body).tools
        return json(200, taskItem())
      })
    const { wrapper } = await mountDetail(fake)

    await wrapper.get('[data-test="edit-policy"]').trigger('click')
    const boxes = wrapper.findAll('form[data-test="policy-form"] input[type="checkbox"]')
    expect(boxes.map((box) => (box.element as HTMLInputElement).value)).toEqual(['agent.*', 'agent.extract', 'mail.*', 'rss.*'])
    await wrapper.get('input[value="rss.*"]').setValue(true)
    await wrapper.get('form[data-test="policy-form"]').trigger('submit')
    await flushPromises()

    expect(JSON.parse(fake.request('PUT', '/api/v1/tasks/t-1/tool-policy')!.body)).toEqual({ tools: ['agent.extract', 'mail.*', 'rss.*'] })
    expect(fake.count('GET', '/api/v1/tasks/t-1')).toBe(2)
    expect(wrapper.get('[data-test="tool-policy"]').text()).toContain('rss.*')
  })

  it('reports a missing task', async () => {
    const fake = new FakeServer().on('GET', '/api/v1/me', 200, me).on('GET', '/api/v1/tasks/t-1', 404, { status: 404, code: 'tasks.not_found' })
    const { wrapper } = await mountDetail(fake)

    expect(wrapper.text()).toContain('This task no longer exists.')
  })

  it('keeps the dialog open with the error when cancelling fails', async () => {
    const fake = server().on('POST', '/api/v1/tasks/t-1/cancel', 409, { status: 409, code: 'task.finished', detail: 'The task already finished.' })
    const { wrapper } = await mountDetail(fake)

    await wrapper.get('[data-test="cancel"]').trigger('click')
    ;(document.body.querySelector('[data-test="cancel-confirm"]') as HTMLButtonElement).click()
    await flushPromises()

    expect(document.body.querySelector('[role="dialog"]')?.textContent).toContain('The task already finished.')
  })
})
