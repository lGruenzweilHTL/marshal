import type { LiveEventMap, LiveEventType } from '../live/events'
import type { LiveEvents } from '../live/liveEvents'

type AnyHandler = (payload: never) => void

/** Live events a test publishes by hand. */
export class FakeLiveEvents implements LiveEvents {
  private readonly handlers: { topic: string; type: string; handler: AnyHandler }[] = []
  private readonly reconnectHandlers = new Set<() => void>()

  on<T extends LiveEventType>(topic: string, type: T, handler: (payload: LiveEventMap[T]) => void): () => void {
    const entry = { topic, type, handler: handler as AnyHandler }
    this.handlers.push(entry)
    return () => this.handlers.splice(this.handlers.indexOf(entry), 1)
  }

  publish<T extends LiveEventType>(topic: string, type: T, payload: LiveEventMap[T]) {
    for (const entry of this.handlers.filter((candidate) => candidate.topic === topic && candidate.type === type))
      (entry.handler as (payload: LiveEventMap[T]) => void)(payload)
  }

  onReconnect(handler: () => void): () => void {
    this.reconnectHandlers.add(handler)
    return () => this.reconnectHandlers.delete(handler)
  }

  /** Acts as if the stream dropped and opened again. */
  reconnect() {
    for (const handler of [...this.reconnectHandlers]) handler()
  }

  get subscriptions() {
    return this.handlers.length
  }
}
