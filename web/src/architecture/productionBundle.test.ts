import { join } from 'node:path'
import { build, type Rollup } from 'vite'
import { describe, expect, it } from 'vitest'

const web = join(__dirname, '..', '..')

describe('production bundle', () => {
  it('contains no module from web/src/preview/ (no fake API, no sample data)', async () => {
    const output = (await build({ root: web, configFile: join(web, 'vite.config.ts'), mode: 'production', logLevel: 'silent', build: { write: false } })) as
      | Rollup.RollupOutput
      | Rollup.RollupOutput[]
    const modules = [output]
      .flat()
      .flatMap((bundle) => bundle.output)
      .flatMap((item) => (item.type === 'chunk' ? item.moduleIds : []))

    expect(modules.length).toBeGreaterThan(50)
    expect(modules.filter((id) => id.replaceAll('\\', '/').includes('/src/preview/'))).toEqual([])
    expect(modules.filter((id) => id.replaceAll('\\', '/').includes('/src/testing/'))).toEqual([])
  }, 120_000)
})
