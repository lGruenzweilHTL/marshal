import { describe, expect, it } from 'vitest'
import { createLiveEvents, type EventSourceLike } from './liveEvents'

class FakeEventSource implements EventSourceLike {
  static opened: FakeEventSource[] = []
  readonly listeners = new Map<string, (event: MessageEvent<string>) => void>()
  closed = false
  constructor(readonly url: string) {
    FakeEventSource.opened.push(this)
  }
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void) {
    this.listeners.set(type, listener)
  }
  close() {
    this.closed = true
  }
  fire(type: string) {
    this.listeners.get(type)?.(new MessageEvent(type))
  }
  emit(type: string, message: unknown) {
    this.listeners.get(type)?.(new MessageEvent(type, { data: JSON.stringify(message) }))
  }
}

describe('createLiveEvents', () => {
  it('delivers typed payloads of the subscribed event type', () => {
    FakeEventSource.opened = []
    const live = createLiveEvents('https://marshal.test', (url) => new FakeEventSource(url))
    const received: unknown[] = []

    live.on('runs', 'run.completed', (payload) => received.push(payload.outcome))
    const source = FakeEventSource.opened[0]!
    source.emit('run.completed', { topic: 'runs', type: 'run.completed', id: '1', payload: { runId: 'r', outcome: 'ok', cost: 0, endedAt: '' } })
    source.emit('run.started', { topic: 'runs', type: 'run.started', id: '2', payload: {} })

    expect(source.url).toBe('https://marshal.test/api/v1/events?topics=runs')
    expect(received).toEqual(['ok'])
  })

  it('shares one connection per topic set and closes it after the last unsubscribe', () => {
    FakeEventSource.opened = []
    const live = createLiveEvents('https://marshal.test', (url) => new FakeEventSource(url))

    const first = live.on('runs', 'run.completed', () => {})
    const second = live.on('runs', 'run.started', () => {})
    expect(FakeEventSource.opened).toHaveLength(1)

    first()
    expect(FakeEventSource.opened[0]!.closed).toBe(false)
    second()
    expect(FakeEventSource.opened[0]!.closed).toBe(true)
  })

  it('reports a reconnect after the connection dropped, not the first open', () => {
    FakeEventSource.opened = []
    const live = createLiveEvents('https://marshal.test', (url) => new FakeEventSource(url))
    let reconnects = 0
    live.on('conversations', 'conversation.result', () => {})
    const stop = live.onReconnect(() => reconnects++)
    const source = FakeEventSource.opened[0]!

    source.fire('open')
    expect(reconnects).toBe(0)
    source.fire('error')
    source.fire('open')
    expect(reconnects).toBe(1)

    stop()
    source.fire('error')
    source.fire('open')
    expect(reconnects).toBe(1)
  })
})
