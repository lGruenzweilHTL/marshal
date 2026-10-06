import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const src = join(__dirname, '..')

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name)
    if (statSync(path).isDirectory()) return files(path)
    return /\.(vue|ts)$/.test(name) && !/\.test\.ts$/.test(name) && !name.endsWith('.d.ts') ? [path] : []
  })
}

/** Files outside `web/src/api/` (and test helpers) that reach the raw `openapi-fetch` client instead of `call()`. */
function rawClientUsers(): string[] {
  return files(src)
    .map((path) => relative(src, path).replaceAll('\\', '/'))
    .filter((path) => !path.startsWith('api/') && !path.startsWith('testing/') && !path.startsWith('preview/'))
    .filter((path) => /useApiSession\(|\.raw\.(GET|POST|PUT|PATCH|DELETE)\b|session\.api\b/.test(readFileSync(join(src, path), 'utf8')))
}

describe('architecture', () => {
  it('no file outside web/src/api/ uses the raw API client', () => {
    expect(rawClientUsers()).toEqual([])
  })
})
