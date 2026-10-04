import { shallowRef, type Ref } from 'vue'
import type { ApiError, ApiResult } from './errors'

export interface Submit<A extends unknown[], R> {
  readonly busy: Readonly<Ref<boolean>>
  /** Why the last submit failed; cleared when the next one starts. */
  readonly error: Readonly<Ref<ApiError | null>>
  /** Runs the action unless one is running; the form keeps its values whatever the result. */
  submit(...args: A): Promise<ApiResult<R>>
}

/** Form submission state: busy while the action runs, the error of the last failed run. */
export function useSubmit<A extends unknown[], R>(action: (...args: A) => Promise<ApiResult<R>>): Submit<A, R> {
  const busy = shallowRef(false)
  const error = shallowRef<ApiError | null>(null)
  let running: Promise<ApiResult<R>> | null = null

  async function submit(...args: A): Promise<ApiResult<R>> {
    if (running) return running
    busy.value = true
    error.value = null
    running = action(...args)
    try {
      const result = await running
      error.value = result.error ?? null
      return result
    } finally {
      running = null
      busy.value = false
    }
  }

  return { busy, error, submit }
}
