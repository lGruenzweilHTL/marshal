import { inject, shallowRef, type InjectionKey, type Ref } from 'vue'

export interface Toast {
  id: number
  message: string
}

/** Short confirmations after a successful action ("Saved"); they disappear on their own. */
export interface Toasts {
  readonly items: Readonly<Ref<Toast[]>>
  show(message: string): void
  dismiss(id: number): void
}

export const toastKey: InjectionKey<Toasts> = Symbol('toasts')

export function createToasts(duration = 3500): Toasts {
  const items = shallowRef<Toast[]>([])
  let next = 0
  const toasts: Toasts = {
    items,
    show(message) {
      const id = ++next
      items.value = [...items.value, { id, message }]
      setTimeout(() => toasts.dismiss(id), duration)
    },
    dismiss(id) {
      items.value = items.value.filter((toast) => toast.id !== id)
    },
  }
  return toasts
}

/** The app's toasts; outside the app (isolated component tests) a local, unrendered store. */
export function useToasts(): Toasts {
  return inject(toastKey, null) ?? createToasts()
}
