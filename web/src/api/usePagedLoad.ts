import { computed, shallowRef } from 'vue'
import type { ApiResult } from './errors'
import { useLoad, type Load } from './useLoad'
import { useSubmit } from './useSubmit'

/**
 * A list the API pages with `before` + `limit`: the first page as a {@link Load}, older pages appended by `more`.
 * A full page means there may be older items; the API has no total count.
 */
export function usePagedLoad<T>(fetchPage: (before: string | undefined, limit: number) => Promise<ApiResult<T[]>>, cursor: (item: T) => string, pageSize = 50) {
  const older = shallowRef<T[]>([])
  const lastPage = shallowRef<number | null>(null)
  const first: Load<T[]> = useLoad(() => {
    older.value = []
    lastPage.value = null
    return fetchPage(undefined, pageSize)
  })
  const items = computed(() => [...(first.data.value ?? []), ...older.value])
  const hasMore = computed(() => (lastPage.value ?? first.data.value?.length) === pageSize)
  const more = useSubmit(async () => {
    const last = items.value.at(-1)
    const result = await fetchPage(last ? cursor(last) : undefined, pageSize)
    if (result.data) {
      older.value = [...older.value, ...result.data]
      lastPage.value = result.data.length
    }
    return result
  })
  return { load: first, items, hasMore, more }
}
