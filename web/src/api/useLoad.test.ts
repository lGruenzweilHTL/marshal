import { describe, expect, it } from 'vitest'
import { nextTick } from 'vue'
import type { ApiResult } from './errors'
import { useLoad } from './useLoad'

/** A fetcher whose results the test releases one by one. */
function controlled<T>() {
  const pending: ((result: ApiResult<T>) => void)[] = []
  return {
    fetcher: () => new Promise<ApiResult<T>>((resolve) => pending.push(resolve)),
    resolve: (result: ApiResult<T>) => pending.shift()!(result),
    pending,
  }
}

const flush = () => new Promise((resolve) => setTimeout(resolve))

describe('useLoad', () => {
  it('starts loading and becomes ready with the data', async () => {
    const source = controlled<string[]>()
    const load = useLoad(source.fetcher)
    expect(load.state.value).toBe('loading')

    source.resolve({ data: ['a'] })
    await flush()

    expect(load.state.value).toBe('ready')
    expect(load.data.value).toEqual(['a'])
  })

  it('is empty for an empty list or when isEmpty says so', async () => {
    const list = controlled<string[]>()
    const object = controlled<{ items: string[] }>()
    const fromList = useLoad(list.fetcher)
    const fromObject = useLoad(object.fetcher, { isEmpty: (data) => data.items.length === 0 })

    list.resolve({ data: [] })
    object.resolve({ data: { items: [] } })
    await flush()

    expect(fromList.state.value).toBe('empty')
    expect(fromObject.state.value).toBe('empty')
  })

  it('keeps the error and retries into ready', async () => {
    const source = controlled<string[]>()
    const load = useLoad(source.fetcher)
    source.resolve({ error: { kind: 'network' } })
    await flush()
    expect(load.state.value).toBe('error')
    expect(load.error.value).toEqual({ kind: 'network' })

    load.retry()
    await nextTick()
    expect(load.state.value).toBe('loading')
    source.resolve({ data: ['b'] })
    await flush()

    expect(load.state.value).toBe('ready')
    expect(load.error.value).toBeNull()
  })

  it('refreshes quietly: the old data stays until the new data arrives', async () => {
    const source = controlled<string[]>()
    const load = useLoad(source.fetcher)
    source.resolve({ data: ['a'] })
    await flush()

    load.refresh()
    await nextTick()
    expect(load.state.value).toBe('ready')
    source.resolve({ data: ['a', 'b'] })
    await flush()

    expect(load.data.value).toEqual(['a', 'b'])
  })

  it('ignores a slower earlier response that arrives after a newer one', async () => {
    const source = controlled<string>()
    const load = useLoad(source.fetcher)
    load.retry()
    const [first, second] = source.pending
    second!({ data: 'new' })
    await flush()
    first!({ data: 'old' })
    await flush()

    expect(load.data.value).toBe('new')
  })
})
