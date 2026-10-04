// @vitest-environment node
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { build, type Rollup } from 'vite'
import { afterAll, beforeAll, describe, expect, it } from 'vitest'

const web = join(__dirname, '..', '..')
let outDir = ''
let modules: string[] = []

describe('production build', () => {
  beforeAll(async () => {
    outDir = mkdtempSync(join(tmpdir(), 'marshal-web-'))
    const output = (await build({ root: web, configFile: join(web, 'vite.config.ts'), mode: 'production', logLevel: 'silent', build: { outDir, emptyOutDir: true } })) as
      | Rollup.RollupOutput
      | Rollup.RollupOutput[]
    modules = [output]
      .flat()
      .flatMap((bundle) => bundle.output)
      .flatMap((item) => (item.type === 'chunk' ? item.moduleIds : []))
  }, 180_000)

  afterAll(() => rmSync(outDir, { recursive: true, force: true }))

  it('contains no module from web/src/preview/ or web/src/testing/ (no fake API, no sample data)', () => {
    const posix = modules.map((id) => id.replaceAll('\\', '/'))
    expect(posix.length).toBeGreaterThan(50)
    expect(posix.filter((id) => id.includes('/src/preview/') || id.includes('/src/testing/'))).toEqual([])
  })

  describe('service worker', () => {
    const sw = () => readFileSync(join(outDir, 'sw.js'), 'utf8')
    const precached = () => [...sw().matchAll(/url:\s*"([^"]+)"/g)].map((match) => match[1]!)

    it('precaches the shell assets and the offline page but not index.html', () => {
      expect(precached()).toContain('offline.html')
      expect(precached().some((url) => url.startsWith('assets/') && url.endsWith('.js'))).toBe(true)
      expect(precached().filter((url) => url.endsWith('index.html'))).toEqual([])
    })

    it('has no route for the API or /health: only navigations, network-only', () => {
      const routes = [...sw().matchAll(/registerRoute\(([^,]+)/g)].map((match) => match[1]!)
      expect(routes).toHaveLength(1)
      expect(routes[0]).toContain('"navigate"')
      expect(sw()).toContain('NetworkOnly')
      expect(sw()).not.toMatch(/api\/|\/health|NavigationRoute|createHandlerBoundToURL/)
    })

    it('ships a manifest that installs standalone under any base path', () => {
      const manifest = JSON.parse(readFileSync(join(outDir, 'manifest.webmanifest'), 'utf8')) as Record<string, unknown>
      expect(manifest).toMatchObject({ display: 'standalone', start_url: './', scope: './' })
    })
  })
})
