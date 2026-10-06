import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative } from 'node:path'
import { describe, expect, it } from 'vitest'

const src = join(__dirname, '..')

function files(dir: string): string[] {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name)
    return statSync(path).isDirectory() ? files(path) : [path]
  })
}

/** Style blocks of the app: every component and stylesheet except the generated tokens.css. */
function styles(): { file: string; css: string }[] {
  return files(src)
    .filter((path) => path.endsWith('.vue') || (path.endsWith('.css') && !path.endsWith('tokens.css')))
    .map((path) => ({ file: relative(src, path).replaceAll('\\', '/'), text: readFileSync(path, 'utf8') }))
    .map(({ file, text }) => ({ file, css: file.endsWith('.vue') ? [...text.matchAll(/<style[^>]*>([\s\S]*?)<\/style>/g)].map((match) => match[1]).join('\n') : text }))
}

describe('design system', () => {
  it('has no literal colours or font families outside tokens.css', () => {
    const offenders = styles().flatMap(({ file, css }) => {
      const pattern = file === 'ui/fonts.css' ? /#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(/g : /#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|font-family:/g
      const found = [...css.matchAll(pattern)].map((match) => match[0])
      return found.length ? [`${file}: ${found.join(', ')}`] : []
    })
    expect(offenders).toEqual([])
  })

  it('keeps logo artwork only in ui/logo/', () => {
    const geometry = files(src)
      .filter((file) => /\.(vue|ts|css|html)$/.test(file) && !file.endsWith('.test.ts'))
      .filter((file) => !relative(src, file).replaceAll('\\', '/').startsWith('ui/logo/'))
      .filter((file) => readFileSync(file, 'utf8').includes('M1.6 24V6L8 17.4'))
    expect(geometry).toEqual([])
  })
})
