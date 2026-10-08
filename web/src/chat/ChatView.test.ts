import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import ChatView from './ChatView.vue'
import { eventStream, FakeServer, type RecordedRequest } from '../testing/fakeServer'
import { me, mountOptions } from '../testing/mountApp'

const turns = '/api/v1/conversation/turns'

function sse(event: string, data: unknown) {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`
}

function final(speech: string, conversationId = 's-1', continueConversation = false) {
  return sse('final', { speech, continueConversation, conversationId })
}

async function mountChat(server: FakeServer) {
  const { options } = await mountOptions(server.on('GET', '/api/v1/me', 200, me).antiforgery('t'), '/chat')
  const wrapper = mount(ChatView, { ...options, attachTo: document.body })
  await flushPromises()
  return wrapper
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

    expect(wrapper.findAll('[data-role="assistant"]').at(-1)!.get('[data-test="step"]').text()).toBe('Checking the printer…')
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
})
