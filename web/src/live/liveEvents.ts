import { inject, onBeforeUnmount, type InjectionKey } from 'vue'
import type { LiveEventMap, LiveEventType, LiveMessage } from './events'

/** Subscriptions to live events; all views of a tab share one server-sent event connection. */
export interface LiveEvents {
  /** Calls `handler` for each `type` event on `topic`; returns the unsubscribe function. */
  on<T extends LiveEventType>(topic: string, type: T, handler: (payload: LiveEventMap[T]) => void): () => void
}

/** The part of `EventSource` the live connection uses. */
export interface EventSourceLike {
  addEventListener(type: string, listener: (event: MessageEvent<string>) => void): void
  close(): void
}

interface Subscription {
  topic: string
  type: string
  handler: (payload: never) => void
}

/**
 * Live events over one `EventSource` for the union of subscribed topics. The connection reopens when the topic set
 * changes and closes with the last subscription; `EventSource` reconnects by itself after network errors.
 */
export function createLiveEvents(
  baseUrl: string,
  open: (url: string) => EventSourceLike = (url) => new EventSource(url, { withCredentials: true }),
): LiveEvents {
  const subscriptions = new Set<Subscription>()
  let source: EventSourceLike | null = null
  let topics = ''
  const listened = new Set<string>()

  function dispatch(event: MessageEvent<string>) {
    const message = JSON.parse(event.data) as LiveMessage
    for (const subscription of subscriptions)
      if (subscription.topic === message.topic && subscription.type === message.type) (subscription.handler as (payload: unknown) => void)(message.payload)
  }

  function reconnect() {
    const wanted = [...new Set([...subscriptions].map((subscription) => subscription.topic))].sort().join(',')
    const types = new Set([...subscriptions].map((subscription) => subscription.type))
    if (wanted !== topics) {
      source?.close()
      source = wanted ? open(`${baseUrl}/api/v1/events?topics=${encodeURIComponent(wanted)}`) : null
      topics = wanted
      listened.clear()
    }
    for (const type of types) {
      if (source && !listened.has(type)) {
        source.addEventListener(type, dispatch)
        listened.add(type)
      }
    }
  }

  return {
    on(topic, type, handler) {
      const subscription: Subscription = { topic, type, handler: handler as (payload: never) => void }
      subscriptions.add(subscription)
      reconnect()
      return () => {
        subscriptions.delete(subscription)
        reconnect()
      }
    },
  }
}

export const liveKey: InjectionKey<LiveEvents> = Symbol('live')

/** Subscribes for the lifetime of the calling component. */
export function useLiveEvent<T extends LiveEventType>(topic: string, type: T, handler: (payload: LiveEventMap[T]) => void) {
  const live = inject(liveKey)
  if (!live) throw new Error('No live events provided.')
  onBeforeUnmount(live.on(topic, type, handler))
}
