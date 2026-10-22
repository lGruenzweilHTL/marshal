import { inject, onScopeDispose, shallowRef, type InjectionKey, type Ref } from 'vue'
import type { Api } from '../api/client'
import type { LiveEvents } from '../live/liveEvents'

/**
 * How many Inbox items need the user: pending approvals, open change and skill proposals and critical notifications newer than the
 * last visit to the Inbox (kept per device; the API has no read state).
 */
export interface InboxCount {
  readonly count: Readonly<Ref<number>>
  refresh(): Promise<void>
  /** The user opened the Inbox: critical notifications up to now count as seen. */
  markSeen(): void
}

export const inboxCountKey: InjectionKey<InboxCount> = Symbol('inboxCount')

const seenKey = 'marshal.inbox.seen'

function seenAt(): number {
  try {
    return Number(localStorage.getItem(seenKey) ?? 0)
  } catch {
    return 0
  }
}

export function createInboxCount(api: Api, live?: LiveEvents, interval = 60_000): InboxCount {
  const count = shallowRef(0)
  let critical: string[] = []

  async function refresh() {
    const [approvals, proposals, skills, notifications] = await Promise.all([
      api.call((client) => client.GET('/api/v1/approvals')),
      api.call((client) => client.GET('/api/v1/proposals')),
      api.call((client) => client.GET('/api/v1/skill-proposals')),
      api.call((client) => client.GET('/api/v1/notifications')),
    ])
    // A failed refresh keeps the last count; the badge is a hint, the Inbox shows the real state and its errors.
    if (approvals.error || proposals.error || notifications.error) return
    critical = notifications.data.filter((item) => item.priority === 'critical').map((item) => item.createdAt)
    const seen = seenAt()
    // Skill proposals are optional in the count: a failing list leaves them out instead of freezing the badge.
    const openSkills = skills.data?.filter((item) => item.status === 'open').length ?? 0
    count.value = approvals.data.length + proposals.data.filter((item) => item.status === 'open').length + openSkills + critical.filter((at) => Date.parse(at) > seen).length
  }

  const timer = setInterval(() => void refresh(), interval)
  const off = live?.on('notifications', 'notification.created', () => void refresh())
  onScopeDispose(() => {
    clearInterval(timer)
    off?.()
  })

  return {
    count,
    refresh,
    markSeen() {
      try {
        localStorage.setItem(seenKey, String(Date.now()))
      } catch {
        // Without storage, critical notifications keep counting until they are older than the list.
      }
      void refresh()
    },
  }
}

export function useInboxCount(): InboxCount {
  const inbox = inject(inboxCountKey)
  if (!inbox) throw new Error('No inbox count provided.')
  return inbox
}

/** The Inbox count when inside the app shell, else null (deep-link screens tested alone, the preview of a component). */
export function useInboxCountOptional(): InboxCount | null {
  return inject(inboxCountKey, null)
}
