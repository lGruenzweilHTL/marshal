import { shallowRef, type Ref } from 'vue'
import type { ApiError, ApiResult } from './errors'

export type LoadState = 'loading' | 'ready' | 'empty' | 'error'

export interface Load<T> {
  readonly state: Readonly<Ref<LoadState>>
  /** The last data that arrived (kept while a refresh runs). */
  readonly data: Readonly<Ref<T | null>>
  readonly error: Readonly<Ref<ApiError | null>>
  /** Loads again from the loading state (the retry action of an error state). */
  retry(): void
  /** Loads again in the background; the current data stays on screen until the new data arrives. */
  refresh(): void
}

export interface LoadOptions<T> {
  /** Whether the data counts as empty; by default an empty array is empty and anything else is not. */
  isEmpty?(data: T): boolean
}

/**
 * Runs `fetcher` right away and exposes the view states: loading, ready, empty and error with a retry. A response that
 * arrives after a newer request was started is ignored.
 */
export function useLoad<T>(fetcher: () => Promise<ApiResult<T>>, options: LoadOptions<T> = {}): Load<T> {
  const isEmpty = options.isEmpty ?? ((data: T) => Array.isArray(data) && data.length === 0)
  const state = shallowRef<LoadState>('loading')
  const data = shallowRef<T | null>(null)
  const error = shallowRef<ApiError | null>(null)
  let latest = 0

  async function run(quiet: boolean) {
    const request = ++latest
    if (!quiet || state.value === 'error') state.value = 'loading'
    const result = await fetcher()
    if (request !== latest) return
    if (result.error) {
      error.value = result.error
      state.value = 'error'
      return
    }
    error.value = null
    data.value = result.data
    state.value = isEmpty(result.data) ? 'empty' : 'ready'
  }

  void run(false)
  return { state, data, error, retry: () => void run(false), refresh: () => void run(true) }
}
