import { onScopeDispose, shallowRef, type Ref } from 'vue'

/** The current time, updated every `interval` ms while the calling component lives (countdowns, "5 min ago"). */
export function useNow(interval = 1000): Readonly<Ref<number>> {
  const now = shallowRef(Date.now())
  const timer = setInterval(() => (now.value = Date.now()), interval)
  onScopeDispose(() => clearInterval(timer))
  return now
}
