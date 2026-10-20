import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ChatView from './ChatView.vue'
import { eventStream, FakeServer, json, type RecordedRequest } from '../testing/fakeServer'
import { conversationSummary } from '../testing/fixtures'
import { me, mountOptions } from '../testing/mountApp'
import { useViewport } from '../testing/viewport'
import type { Schemas } from '../api/client'

const turns = '/api/v1/conversation/turns'

function sse(event: string, data: unknown) {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`
}

function final(speech: string, conversationId = 's-1', continueConversation = false) {
  return sse('final', { speech, continueConversation, conversationId })
}

async function mountChat(server: FakeServer, path = '/chat') {
  const mounted = await mountOptions(server.on('GET', '/api/v1/me', 200, me).antiforgery('t'), path)
  const wrapper = mount(ChatView, { ...mounted.options, attachTo: document.body })
  await flushPromises()
  return Object.assign(wrapper, { router: mounted.router, live: mounted.live })
}

type Entry = Schemas['ConversationDetail']['entries'][number]

function turn(overrides: Partial<Extract<Entry, { kind?: 'turn' }>> = {}): Entry {
  return {
    kind: 'turn',
    runId: 'r-1',
    at: '2026-10-12T08:00:00Z',
    inputMode: 'text',
    satelliteId: null,
    areaId: null,
    input: 'hello',
    reply: 'Hi there.',
    continueConversation: false,
    artifacts: [],
    outcome: 'ok',
    reasoning: null,
    reasoningWithheld: false,
    tools: [],
    ...overrides,
  }
}

function detail(id: string, entries: Entry[], overrides: Partial<Schemas['ConversationSummary']> = {}): Schemas['ConversationDetail'] {
  return { conversation: conversationSummary({ id, turns: entries.length, ...overrides }), entries }
}

async function send(wrapper: Awaited<ReturnType<typeof mountChat>>, text: string) {
  await wrapper.get('textarea').setValue(text)
  await wrapper.get('form').trigger('submit')
}

function body(request: RecordedRequest | undefined) {
  return JSON.parse(request!.body) as Record<string, unknown>
}

describe('ChatView', () => {
  it('shows the reply growing as delta events arrive', async () => {
    let release!: () => void
    const gate = new Promise<void>((resolve) => (release = resolve))
    const encoder = new TextEncoder()
    const server = new FakeServer().handle('POST', turns, () => {
      const stream = new ReadableStream<Uint8Array>({
        async start(controller) {
          controller.enqueue(encoder.encode(sse('delta', { text: 'It is ' })))
          await gate
          controller.enqueue(encoder.encode(sse('delta', { text: 'sunny.' }) + final('It is sunny.')))
          controller.close()
        },
      })
      return new Response(stream, { headers: { 'Content-Type': 'text/event-stream' } })
    })
    const wrapper = await mountChat(server)

    await send(wrapper, 'weather?')
    await flushPromises()
    expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.text()).toBe('It is')

    release()
    await flushPromises()
    expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.text()).toBe('It is sunny.')
    expect(wrapper.get('[data-role="user"]').text()).toBe('weather?')
    expect(body(server.request('POST', turns))).toMatchObject({ text: 'weather?', inputMode: 'text', conversationId: null })
  })

  it('shows status events while the turn runs', async () => {
    let release!: () => void
    const gate = new Promise<void>((resolve) => (release = resolve))
    const encoder = new TextEncoder()
    const server = new FakeServer().handle('POST', turns, () => {
      const stream = new ReadableStream<Uint8Array>({
        async start(controller) {
          controller.enqueue(encoder.encode(sse('status', { text: 'Checking the printer…' })))
          await gate
          controller.enqueue(encoder.encode(final('Printing.')))
          controller.close()
        },
      })
      return new Response(stream, { headers: { 'Content-Type': 'text/event-stream' } })
    })
    const wrapper = await mountChat(server)

    await send(wrapper, 'printer?')
    await flushPromises()
    expect(wrapper.get('[role="status"]').text()).toBe('Checking the printer…')

    release()
    await flushPromises()
    expect(wrapper.find('[role="status"]').exists()).toBe(false)
  })

  it('continues the session and starts a new one on request', async () => {
    const server = new FakeServer().handle('POST', turns, () => eventStream([final('ok', 's-42')]))
    const wrapper = await mountChat(server)

    await send(wrapper, 'one')
    await flushPromises()
    await send(wrapper, 'two')
    await flushPromises()
    expect(body(server.request('POST', turns)).conversationId).toBe('s-42')

    await wrapper.get('[data-test="new-chat"]').trigger('click')
    expect(wrapper.findAll('[data-role]')).toHaveLength(0)
    await send(wrapper, 'three')
    await flushPromises()
    expect(body(server.request('POST', turns)).conversationId).toBeNull()
  })

  it('shows a confirmation question with its reason and sends the answer in the same session', async () => {
    const question = 'Should I unlock the front door? It opens the house to anyone outside.'
    const server = new FakeServer().handle('POST', turns, (request) =>
      eventStream([JSON.parse(request.body).text === 'confirm' ? final('Done, it is unlocked.', 's-7') : final(question, 's-7', true)]),
    )
    const wrapper = await mountChat(server)

    await send(wrapper, 'unlock the front door')
    await flushPromises()
    expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.text()).toBe(question)
    expect(wrapper.get('[data-test="awaiting-answer"]').text()).toContain('waiting for your answer')

    await send(wrapper, 'confirm')
    await flushPromises()
    expect(body(server.request('POST', turns))).toMatchObject({ text: 'confirm', conversationId: 's-7' })
    expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.text()).toBe('Done, it is unlocked.')
    expect(wrapper.find('[data-test="awaiting-answer"]').exists()).toBe(false)
  })

  it('answers a confirmation question with the quick replies', async () => {
    const server = new FakeServer().handle('POST', turns, (request) =>
      eventStream([JSON.parse(request.body).text === 'Yes' ? final('Unlocked.', 's-7') : final('Should I unlock the front door?', 's-7', true)]),
    )
    const wrapper = await mountChat(server)

    await send(wrapper, 'unlock the front door')
    await flushPromises()
    await wrapper.get('[data-test="awaiting-answer"] [data-test="yes"]').trigger('click')
    await flushPromises()

    expect(body(server.request('POST', turns))).toMatchObject({ text: 'Yes', conversationId: 's-7' })
    expect(wrapper.findAll('[data-role="user"]').at(-1)!.text()).toBe('Yes')
    expect(wrapper.find('[data-test="awaiting-answer"]').exists()).toBe(false)
  })

  it('keeps the steps of a turn in the reply after it finished', async () => {
    const server = new FakeServer().handle('POST', turns, () => eventStream([sse('status', { text: 'Checking the printer…' }), final('Printing.')]))
    const wrapper = await mountChat(server)

    await send(wrapper, 'printer?')
    await flushPromises()

    expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.get('[data-test="status-step"]').text()).toBe('Checking the printer…')
  })

  describe('reasoning and tool steps', () => {
    function gated(first: string[], rest: string[]) {
      let release!: () => void
      const gate = new Promise<void>((resolve) => (release = resolve))
      const encoder = new TextEncoder()
      const server = new FakeServer().handle('POST', turns, () => {
        const stream = new ReadableStream<Uint8Array>({
          async start(controller) {
            controller.enqueue(encoder.encode(first.join('')))
            await gate
            controller.enqueue(encoder.encode(rest.join('')))
            controller.close()
          },
        })
        return new Response(stream, { headers: { 'Content-Type': 'text/event-stream' } })
      })
      return { server, release }
    }

    const tool = { id: 'call-1', tool: 'printer.status', arguments: 'printer=voron' }

    it('opens the reasoning while it streams and collapses it when the reply starts', async () => {
      const { server, release } = gated(
        [sse('reasoning', { text: 'Check the printer ' }), sse('reasoning', { text: 'first.' })],
        [sse('delta', { text: 'Printing.' }), final('Printing.')],
      )
      const wrapper = await mountChat(server)

      await send(wrapper, 'printer?')
      await flushPromises()
      const reply = () => wrapper.findAll('[data-role="assistant"]').at(-1)!
      expect(reply().get('details').attributes('open')).toBeDefined()
      expect(reply().get('[data-test="reasoning-text"]').text()).toBe('Check the printer first.')
      expect(reply().text()).not.toContain('Thinking… Check')
      expect(reply().find('.thinking').exists()).toBe(false)

      release()
      await flushPromises()
      expect(reply().get('details').attributes('open')).toBeUndefined()
      expect(reply().get('.markdown').text()).toBe('Printing.')
    })

    it('shows the thinking indicator only until the first event and updates a tool line in place', async () => {
      const { server, release } = gated([sse('tool', { ...tool, state: 'running' })], [sse('tool', { ...tool, state: 'ok', durationMs: 184, result: 'Printing' }), final('Printing.')])
      const wrapper = await mountChat(server)

      await send(wrapper, 'printer?')
      const reply = () => wrapper.findAll('[data-role="assistant"]').at(-1)!
      expect(reply().find('.thinking').exists()).toBe(true)
      await flushPromises()
      expect(reply().find('.thinking').exists()).toBe(false)
      expect(reply().get('[data-test="tool-step"]').attributes('data-state')).toBe('running')

      release()
      await flushPromises()
      const lines = reply().findAll('[data-test="tool-step"]')
      expect(lines).toHaveLength(1)
      expect(lines[0]!.attributes('data-state')).toBe('ok')
      expect(reply().find('[data-test="reasoning"]').exists()).toBe(false)
    })

    it('separates the reasoning of model calls around a tool call', async () => {
      const server = new FakeServer().handle('POST', turns, () =>
        eventStream([
          sse('reasoning', { text: 'Check the printer.' }),
          sse('tool', { ...tool, state: 'running' }),
          sse('tool', { ...tool, state: 'ok', durationMs: 5 }),
          sse('reasoning', { text: 'It is printing.' }),
          final('Printing.'),
        ]),
      )
      const wrapper = await mountChat(server)

      await send(wrapper, 'printer?')
      await flushPromises()

      expect(wrapper.get('[data-test="reasoning-text"]').element.textContent).toBe('Check the printer.\n\nIt is printing.')
    })

    it('shows the reasoning and tool steps of a reopened conversation collapsed', async () => {
      const server = new FakeServer().on(
        'GET',
        '/api/v1/conversations/s-9',
        200,
        detail('s-9', [
          turn({
            reasoning: 'Check the printer first.',
            tools: [{ tool: 'printer.status', arguments: 'printer=voron', state: 'ok', durationMs: 184, result: 'Printing' }],
          }),
          turn({ runId: 'r-2', reasoningWithheld: true }),
        ]),
      )
      const wrapper = await mountChat(server, '/chat/s-9')

      const [first, second] = wrapper.findAll('[data-role="assistant"]')
      expect(first!.get('details').attributes('open')).toBeUndefined()
      expect(first!.get('[data-test="reasoning-text"]').text()).toBe('Check the printer first.')
      expect(first!.get('[data-test="tool-label"]').text()).toBe('Printer status')
      expect(second!.find('[data-test="reasoning-withheld"]').exists()).toBe(true)
    })
  })

  it('puts the text back into the composer when sending fails', async () => {
    const server = new FakeServer().handle('POST', turns, () => Promise.reject(new TypeError('Failed to fetch')))
    const wrapper = await mountChat(server)

    await send(wrapper, 'hello?')
    await flushPromises()

    expect(wrapper.text()).toContain('Marshal could not be reached')
    expect((wrapper.get('textarea').element as HTMLTextAreaElement).value).toBe('hello?')
    expect(wrapper.findAll('[data-role="user"]')).toHaveLength(0)
  })

  it('renders an external image in a reply as a link, never as an image', async () => {
    const server = new FakeServer().handle('POST', turns, () => eventStream([final('![x](https://evil.example/?q=secret)')]))
    const wrapper = await mountChat(server)

    await send(wrapper, 'hi')
    await flushPromises()

    const reply = wrapper.findAll('[data-role="assistant"]').at(-1)!
    expect(reply.find('.markdown img').exists()).toBe(false)
    expect(reply.get('a').attributes('href')).toBe('https://evil.example/?q=secret')
  })

  it('shows the problem when the turn is rejected', async () => {
    const server = new FakeServer().on('POST', turns, 404, { code: 'session.not_found', detail: 'The session was not found.' })
    const wrapper = await mountChat(server)

    await send(wrapper, 'hi')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toContain('The session was not found.')
  })

  it('shows image artifacts inline and other kinds as cards that open the viewer', async () => {
    const server = new FakeServer().handle('POST', turns, () =>
      eventStream([
        sse('artifact', { id: 'a1', name: 'first-layer.jpg', kind: 'image', url: 'https://elsewhere.example/a1' }),
        sse('artifact', { id: 'a2', name: 'bracket.stl', kind: 'model3d', url: '/api/v1/artifacts/a2/content' }),
        final('Here they are.'),
      ]),
    )
    const wrapper = await mountChat(server)

    await send(wrapper, 'show me')
    await flushPromises()

    const reply = wrapper.findAll('[data-role="assistant"]').at(-1)!
    expect(reply.get('[data-test="artifact-image"] img').attributes('src')).toBe('https://marshal.test/api/v1/artifacts/a1/content')
    const card = reply.get('[data-test="artifact-card"]')
    expect(card.attributes('data-kind')).toBe('model3d')
    expect(card.text()).toContain('bracket.stl')
    expect(card.text()).toContain('3D model')
    expect(card.attributes('href')).toBe('/artifacts/a2')
  })

  it('shows widget events as live widgets below the reply', async () => {
    const server = new FakeServer()
      .handle('POST', turns, () =>
        eventStream([sse('widget', { kind: 'printer', toolCallId: 'c-1', data: { printer: 'voron', state: printerState({ progress: 0.4 }) } }), final('It is printing.')]),
      )
      .on('GET', '/api/v1/printers/voron', 200, printerState({ progress: 0.4 }))
    const wrapper = await mountChat(server)

    await send(wrapper, 'printer?')
    await flushPromises()

    const reply = wrapper.findAll('[data-role="assistant"]').at(-1)!
    expect(reply.get('[data-widget="printer"] [data-test="progress"]').text()).toBe('40%')
    wrapper.live.publish('printer', 'printer.state', { printer: 'voron', state: printerState({ progress: 0.6 }) })
    await flushPromises()
    expect(reply.get('[data-widget="printer"] [data-test="progress"]').text()).toBe('60%')
  })

  it('shows app events as sandboxed apps below the reply', async () => {
    const server = new FakeServer()
      .handle('POST', turns, () => eventStream([sse('app', { appId: 'app-1', server: 'charts', tool: 'sales_report', toolCallId: 'c-1' }), final('Here is the report.')]))
      .on('GET', '/api/v1/apps/app-1', 200, { appId: 'app-1', server: 'charts', tool: 'sales_report', toolCallId: 'c-1', arguments: {}, result: { content: [] }, available: true })
    const wrapper = await mountChat(server)

    await send(wrapper, 'sales report')
    await flushPromises()

    const reply = wrapper.findAll('[data-role="assistant"]').at(-1)!
    expect(reply.get('[data-app="app-1"] iframe').attributes('sandbox')).toBe('allow-scripts')
  })

  describe('history', () => {
    it('renders stored widgets with the current state when a conversation is reopened', async () => {
      const server = new FakeServer()
        .on('GET', '/api/v1/conversations/s-7', 200, detail('s-7', [turn({ widgets: [{ kind: 'printer', callId: 'c-1', data: { printer: 'voron', state: printerState({ progress: 0.1 }) } }] })]))
        .on('GET', '/api/v1/printers/voron', 200, printerState({ progress: 0.8 }))
      const wrapper = await mountChat(server, '/chat/s-7')

      const reply = wrapper.findAll('[data-role="assistant"]').at(-1)!
      expect(reply.get('[data-widget="printer"] [data-test="progress"]').text()).toBe('80%')
    })

    it('renders a stored app again, or the unavailable card when it can no longer be shown', async () => {
      const apps = [
        { appId: 'app-1', server: 'charts', tool: 'sales_report', callId: 'c-1' },
        { appId: 'app-2', server: 'gone', tool: 'report', callId: 'c-2' },
      ]
      const server = new FakeServer()
        .on('GET', '/api/v1/conversations/s-8', 200, detail('s-8', [turn({ apps })]))
        .on('GET', '/api/v1/apps/app-1', 200, { appId: 'app-1', server: 'charts', tool: 'sales_report', toolCallId: 'c-1', arguments: {}, result: { content: [] }, available: true })
        .on('GET', '/api/v1/apps/app-2', 200, { appId: 'app-2', server: 'gone', tool: 'report', toolCallId: 'c-2', arguments: {}, result: null, available: false })
      const wrapper = await mountChat(server, '/chat/s-8')

      const reply = wrapper.findAll('[data-role="assistant"]').at(-1)!
      expect(reply.find('[data-app="app-1"] iframe').exists()).toBe(true)
      expect(reply.find('[data-app="app-2"] iframe').exists()).toBe(false)
      expect(reply.get('[data-app="app-2"] [data-test="app-unavailable"]').text()).toBe('This interactive view is no longer available.')
    })

    it('loads /chat/{id} and renders its turns with voice marks', async () => {
      const server = new FakeServer().on(
        'GET',
        '/api/v1/conversations/s-9',
        200,
        detail('s-9', [turn({ inputMode: 'voice', satelliteId: 'assist_satellite.kitchen', input: 'is the printer done?', reply: 'Not yet.' }), turn({ runId: 'r-2', input: 'thanks', reply: 'Sure.' })]),
      )
      const wrapper = await mountChat(server, '/chat/s-9')

      const users = wrapper.findAll('[data-role="user"]')
      expect(users.map((user) => user.get('.bubble').text())).toEqual(['is the printer done?', 'thanks'])
      expect(users[0]!.get('[data-test="spoken"]').text()).toContain('kitchen')
      expect(users[1]!.find('[data-test="spoken"]').exists()).toBe(false)
      expect(wrapper.findAll('[data-role="assistant"]').map((reply) => reply.text())).toEqual(['Not yet.', 'Sure.'])
    })

    it('continues a loaded web conversation in the same session', async () => {
      const server = new FakeServer().on('GET', '/api/v1/conversations/s-9', 200, detail('s-9', [turn()])).handle('POST', turns, () => eventStream([final('ok', 's-9')]))
      const wrapper = await mountChat(server, '/chat/s-9')

      await send(wrapper, 'again')
      await flushPromises()

      expect(body(server.request('POST', turns)).conversationId).toBe('s-9')
    })

    it('shows a note and a new chat action instead of the composer for read-only conversations', async () => {
      const server = new FakeServer().on(
        'GET',
        '/api/v1/conversations/v-1',
        200,
        detail('v-1', [turn({ inputMode: 'voice', satelliteId: 'assist_satellite.kitchen' })], { source: 'voice', canContinue: false }),
      )
      const wrapper = await mountChat(server, '/chat/v-1')

      expect(wrapper.find('form.composer').exists()).toBe(false)
      expect(wrapper.get('[data-test="read-only"]').text()).toContain('spoken')
      await wrapper.get('[data-test="read-only"] [data-test="read-only-new-chat"]').trigger('click')
      await flushPromises()
      expect(wrapper.router.currentRoute.value.path).toBe('/chat')
      expect(wrapper.find('form.composer').exists()).toBe(true)
    })

    it('replaces the URL with the new session after the first turn of a new chat', async () => {
      useViewport('desktop')
      const server = new FakeServer()
        .on('GET', '/api/v1/conversations', 200, [])
        .handle('POST', turns, () => eventStream([final('Hello!', 's-new')]))
      const wrapper = await mountChat(server)
      const listLoads = server.count('GET', '/api/v1/conversations')

      await send(wrapper, 'hi')
      await flushPromises()

      expect(wrapper.router.currentRoute.value.path).toBe('/chat/s-new')
      expect(server.count('GET', '/api/v1/conversations/s-new')).toBe(0)
      expect(server.count('GET', '/api/v1/conversations')).toBeGreaterThan(listLoads)
      expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.text()).toBe('Hello!')
    })

    it('says the conversation was not found and offers a new chat', async () => {
      const server = new FakeServer().on('GET', '/api/v1/conversations/gone', 404, { code: 'conversation.not_found', detail: 'The conversation does not exist.' })
      const wrapper = await mountChat(server, '/chat/gone')

      expect(wrapper.text()).toContain('Conversation not found')
      expect(wrapper.find('form.composer').exists()).toBe(false)
      await wrapper.get('[data-test="not-found-new-chat"]').trigger('click')
      await flushPromises()
      expect(wrapper.router.currentRoute.value.path).toBe('/chat')
    })

    it('lists the conversations next to the chat on desktop', async () => {
      useViewport('desktop')
      const server = new FakeServer().on('GET', '/api/v1/conversations', 200, [conversationSummary({ id: 's-1', title: 'Printer status' })])
      const wrapper = await mountChat(server)

      expect(wrapper.get('[data-conversation="s-1"]').text()).toContain('Printer status')
    })

    it('opens the conversation list from the header on phones', async () => {
      useViewport('phone')
      const wrapper = await mountChat(new FakeServer())

      expect(wrapper.find('[data-conversation]').exists()).toBe(false)
      expect(wrapper.get('[data-test="history"]').attributes('href')).toBe('/chat/history')
    })
  })

  describe('background results', () => {
    const result = { sessionId: 's-9', taskId: 't-1', resultAt: '2026-10-12T08:05:00Z', body: 'The printer finished at 14:02.' }

    it('shows a stored background result as a marked reply', async () => {
      const server = new FakeServer().on(
        'GET',
        '/api/v1/conversations/s-9',
        200,
        detail('s-9', [turn({ reply: 'Working on it.', outcome: 'backgrounded' }), { kind: 'backgroundResult', taskId: 't-1', at: result.resultAt, body: result.body }]),
      )
      const wrapper = await mountChat(server, '/chat/s-9')

      const last = wrapper.findAll('[data-role="assistant"]').at(-1)!
      expect(last.get('[data-test="background"]').text()).toBe('Background result')
      expect(last.text()).toContain('The printer finished at 14:02.')
    })

    it('appends a live result for the open session once', async () => {
      const server = new FakeServer().on('GET', '/api/v1/conversations/s-9', 200, detail('s-9', [turn({ reply: 'Working on it.', outcome: 'backgrounded' })]))
      const wrapper = await mountChat(server, '/chat/s-9')

      wrapper.live.publish('conversations', 'conversation.result', result)
      wrapper.live.publish('conversations', 'conversation.result', result)
      await flushPromises()

      const replies = wrapper.findAll('[data-role="assistant"]')
      expect(replies).toHaveLength(2)
      expect(replies[1]!.find('[data-test="background"]').exists()).toBe(true)
      expect(replies[1]!.text()).toContain('The printer finished at 14:02.')
    })

    it('ignores a result for another session', async () => {
      const server = new FakeServer().on('GET', '/api/v1/conversations/s-2', 200, detail('s-2', [turn()]))
      const wrapper = await mountChat(server, '/chat/s-2')

      wrapper.live.publish('conversations', 'conversation.result', result)
      await flushPromises()

      expect(wrapper.findAll('[data-role="assistant"]')).toHaveLength(1)
    })

    it('reloads the conversation after the live stream reconnected', async () => {
      let entries: Entry[] = [turn({ reply: 'Working on it.', outcome: 'backgrounded' })]
      const server = new FakeServer().handle('GET', '/api/v1/conversations/s-9', () => json(200, detail('s-9', entries)))
      const wrapper = await mountChat(server, '/chat/s-9')
      entries = [...entries, { kind: 'backgroundResult', taskId: 't-1', at: result.resultAt, body: result.body }]

      wrapper.live.reconnect()
      await flushPromises()

      expect(server.count('GET', '/api/v1/conversations/s-9')).toBe(2)
      expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.text()).toContain('The printer finished at 14:02.')
    })
  })
})

describe('ChatView input requests', () => {
  it('shows the form of an input request the reply asked for', async () => {
    const request = {
      id: 'i-1',
      serverId: 'paperless',
      tool: 'paperless.upload',
      mode: 'form',
      message: 'Which correspondent?',
      requestedSchema: { type: 'object', properties: { correspondent: { type: 'string' } }, required: ['correspondent'] },
      url: null,
      sessionId: 's-1',
      taskId: 't-1',
      status: 'pending',
      createdAt: '2026-10-16T08:00:00Z',
      expiresAt: '2026-10-16T08:10:00Z',
    }
    const server = new FakeServer()
      .handle('POST', turns, () => eventStream([sse('input_request', { id: 'i-1' }), sse('delta', { text: 'Please answer above.' }), final('Please answer above.')]))
      .on('GET', '/api/v1/approvals/inputs/i-1', 200, request)
    const wrapper = await mountChat(server)

    await send(wrapper, 'file the letter')
    await flushPromises()

    expect(wrapper.get('[data-input="i-1"]').text()).toContain('Which correspondent?')
    expect(wrapper.find('[data-input="i-1"] input[name="correspondent"]').exists()).toBe(true)
  })
})
