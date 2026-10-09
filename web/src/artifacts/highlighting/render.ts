import { highlight } from './highlighter'
import { languageForAlias, type Span } from './languages'

const escapes: Record<string, string> = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }
const escape = (text: string) => text.replace(/[&<>"]/g, (character) => escapes[character]!)

/** One line as HTML: the text escaped, each span wrapped in `<span class="tok-<token>">` (colors in syntax.css). */
export function lineHtml(text: string, spans: readonly Span[]): string {
  let html = ''
  let column = 0
  for (const [start, length, token] of spans) {
    if (start < column) continue
    html += escape(text.slice(column, start)) + `<span class="tok-${token}">${escape(text.slice(start, start + length))}</span>`
    column = start + length
  }
  return html + escape(text.slice(column))
}

/** Highlighted HTML for a Markdown fence (markdown-it `highlight`); empty for an unknown language, so markdown-it escapes it. */
export function fenceHtml(code: string, info: string): string {
  const language = languageForAlias(info.split(/[ {]/)[0] ?? '')
  if (!language || info.trim() === '') return ''
  const lines = code.replace(/\n$/, '').split('\n')
  return highlight(language, lines)
    .map((spans, index) => lineHtml(lines[index]!, spans))
    .join('\n')
}

/** Text lines as the highlighter sees them: CRLF normalized, a final newline dropped, tabs as four spaces. */
export function splitLines(text: string): string[] {
  return text.replace(/\r\n/g, '\n').replace(/\n$/, '').split('\n').map((line) => line.replace(/\t/g, '    '))
}
