import type { ApiResult } from '../api/errors'

/** The item with `id` from a list result; null when the list no longer has it (the screen shows "not found"). */
export async function findIn<T extends { id: string }>(list: Promise<ApiResult<T[]>>, id: string): Promise<ApiResult<T | null>> {
  const result = await list
  return result.error ? result : { data: result.data.find((item) => item.id === id) ?? null }
}

export function isNull<T>(value: T | null): boolean {
  return value === null
}
