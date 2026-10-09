import { languageForAlias, type Block, type Language, type Span, type Token } from './languages'

/**
 * What the highlighter carries from one line to the next: an open block, open brackets, an open XML tag, an embedded
 * language (Vue `<script>`) or a Markdown fence, each embedded language with its own state.
 */
export class HighlightState {
  block: Block | null = null
  brackets: string[] = []
  inTag = false
  tagName: string | null = null
  closingTag = false
  lastAttribute: string | null = null
  langAttribute: string | null = null
  embedded: { language: Language; state: HighlightState; closeTag: string } | null = null
  fence: { language: Language | undefined; state: HighlightState; marker: string } | null = null
}

/**
 * Heuristic syntax highlighter driven by `languages.json`. A port of (and kept in step with) the .NET
 * `Marshal.Artifacts.Highlighting.CodeHighlighter`; both run the shared `cases.json`. Not a parser.
 */
export function highlight(language: Language, lines: readonly string[]): Span[][] {
  const state = new HighlightState()
  return lines.map((line) => highlightLine(language, line, state))
}

/** Spans of one line, sorted by start; `state` carries over to the next line. */
export function highlightLine(language: Language, line: string, state: HighlightState): Span[] {
  const spans: Span[] = []
  scanLine(language, line, 0, state, spans)
  return spans.filter((span) => span[1] > 0).sort((a, b) => a[0] - b[0])
}

function scanLine(language: Language, line: string, from: number, state: HighlightState, spans: Span[]): void {
  if (language.mode === 'markdown') markdown(line, state, spans)
  else if (language.mode === 'xml') xml(language, line, from, state, spans)
  else code(language, line, from, state, spans)
}

function code(language: Language, line: string, from: number, state: HighlightState, spans: Span[]): void {
  let index = from
  if (state.block) {
    const open = state.block
    const [end, closed] = blockEnd(line, from, open)
    spans.push([from, end - from, open.token])
    if (!closed) return
    state.block = null
    index = end
  } else if (from === 0 && wholeLine(language, line, spans)) {
    return
  }

  const scanner = new Scanner(language, line, state, spans)
  while (index < line.length) index = scanner.step(index)
}

/** Meta lines (`#include`, `[Fact]`) and INI sections, which color the whole trimmed line. */
function wholeLine(language: Language, line: string, spans: Span[]): boolean {
  const first = firstNonSpace(line)
  if (first === line.length) return false
  let end = line.length
  while (isSpace(line[end - 1]!)) end--
  const trimmed = line.slice(first, end)
  for (const meta of language.metaLines) {
    if (trimmed.startsWith(meta.prefix) && (meta.suffix === undefined || trimmed.endsWith(meta.suffix))) {
      spans.push([first, end - first, 'meta'])
      return true
    }
  }
  if (language.keys === 'ini' && trimmed.length > 1 && trimmed[0] === '[' && trimmed[trimmed.length - 1] === ']') {
    spans.push([first, end - first, 'type'])
    return true
  }
  return false
}

/** The scan of one code line; `step` consumes one token or character and returns the next index. */
class Scanner {
  private readonly selectorEnd: number
  private pending: Token | null = null

  constructor(
    private readonly language: Language,
    private readonly line: string,
    private readonly state: HighlightState,
    private readonly spans: Span[],
  ) {
    this.selectorEnd = language.mode === 'css' ? selectorEnd(line) : -1
  }

  step(index: number): number {
    const { language, line, state } = this
    const current = line[index]!
    const boundary = index === 0 || !isWordChar(line[index - 1]!)
    if (!isSpace(current) && !isWordStart(current)) this.pending = null

    for (const block of language.blocks) {
      if (line.startsWith(block.open, index)) return this.openBlock(index, index, block)
    }

    for (const marker of language.lineComments) {
      if (line.startsWith(marker, index) && (!language.loose || index === 0 || isSpace(line[index - 1]!))) return this.add(index, line.length, 'comment')
    }

    if (boundary && language.stringPrefixes.includes(current)) {
      let quote = index
      while (quote < line.length && quote - index < 3 && language.stringPrefixes.includes(line[quote]!)) quote++
      if (quote < line.length) {
        for (const block of language.blocks) {
          if (block.token === 'string' && line.startsWith(block.open, quote)) return this.openBlock(index, quote, block)
        }
        if (language.quotes.includes(line[quote]!)) {
          const verbatim = [...line.slice(index, quote)].some((prefix) => language.verbatimPrefixes.includes(prefix))
          return this.string(index, quote, verbatim)
        }
      }
    }

    if (language.quotes.includes(current) && (!language.loose || boundary)) return this.string(index, index, false)

    if (language.variables && current === '$' && index + 1 < line.length) {
      const next = line[index + 1]!
      if (next === '{') {
        const close = line.indexOf('}', index + 2)
        return this.add(index, close < 0 ? line.length : close + 1, 'variable')
      }
      if (isWordStart(next)) return this.add(index, this.baseWordEnd(index + 1), 'variable')
      if (isDigit(next) || '@?#*!$'.includes(next)) return this.add(index, index + 2, 'variable')
    }

    if (language.annotations && current === '@' && index + 1 < line.length && isLetter(line[index + 1]!)) {
      let end = index + 1
      while (end < line.length && (isWordChar(line[end]!) || line[end] === '.' || language.wordChars.includes(line[end]!))) end++
      return this.add(index, end, 'meta')
    }

    if (language.mode === 'css') {
      const selector = index < this.selectorEnd
      if (selector && '.#:'.includes(current) && index + 1 < line.length && (isWordStart(line[index + 1]!) || line[index + 1] === '-'))
        return this.add(index, this.wordEnd(index + 1), 'tag')
      if (!selector && current === '#' && index + 1 < line.length && isHexDigit(line[index + 1]!)) {
        let end = index + 1
        while (end < line.length && isAsciiLetterOrDigit(line[end]!)) end++
        return this.add(index, end, 'number')
      }
    }

    if (isDigit(current) && boundary) return this.add(index, this.numberEnd(index), 'number')

    if (isWordStart(current)) return language.mode === 'gcode' ? this.gcodeWord(index) : this.word(index)

    if (language.brackets && '([{'.includes(current)) {
      const token = bracket(state.brackets.length)
      state.brackets.push(current)
      return this.add(index, index + 1, token)
    }

    if (language.brackets && ')]}'.includes(current)) {
      const opening = current === ')' ? '(' : current === ']' ? '[' : '{'
      if (state.brackets.length > 0 && state.brackets[state.brackets.length - 1] === opening) {
        state.brackets.pop()
        return this.add(index, index + 1, bracket(state.brackets.length))
      }
      return this.add(index, index + 1, 'bracket-unmatched')
    }

    return index + 1
  }

  private openBlock(start: number, at: number, block: Block): number {
    const [end, closed] = blockEnd(this.line, at + block.open.length, block)
    if (!closed) this.state.block = block
    return this.add(start, end, block.token)
  }

  private string(start: number, quote: number, verbatim: boolean): number {
    const end = stringEnd(this.line, quote, verbatim)
    let token: Token = 'string'
    if (this.language.keys === 'json' || this.language.keys === 'yaml') {
      const next = skipSpaces(this.line, end)
      if (next < this.line.length && this.line[next] === ':') token = 'property'
    }
    return this.add(start, end, token)
  }

  private gcodeWord(index: number): number {
    const line = this.line
    let end = index + 1
    if ('GMT'.includes(line[index]!.toUpperCase()) && end < line.length && isDigit(line[end]!)) {
      while (end < line.length && (isDigit(line[end]!) || line[end] === '.')) end++
      return this.add(index, end, 'keyword')
    }
    if (end < line.length && (isDigit(line[end]!) || line[end] === '-' || line[end] === '.')) {
      const number = end
      while (end < line.length && (isDigit(line[end]!) || line[end] === '-' || line[end] === '.')) end++
      this.spans.push([number, end - number, 'number'])
      return end
    }
    return this.baseWordEnd(index)
  }

  private word(start: number): number {
    const { language, line } = this
    const end = this.wordEnd(start)
    const word = line.slice(start, end)
    const next = skipSpaces(line, end)
    const after = next < line.length ? line[next]! : ''

    if (language.mode === 'css' && start < this.selectorEnd) return this.add(start, end, 'tag')

    const colonKey = after === ':' && (language.keys !== 'yaml' || next + 1 === line.length || isSpace(line[next + 1]!))
    if ((language.keys === 'yaml' || language.keys === 'json' || language.mode === 'css') && colonKey) return this.add(start, end, 'property')
    if (language.keys === 'ini' && start === firstNonSpace(line) && (after === '=' || after === ':')) return this.add(start, end, 'property')

    if (language.keywords.has(word)) {
      this.pending = language.functionKeywords.has(word) ? 'function' : language.typeKeywords.has(word) ? 'type' : null
      return this.add(start, end, 'keyword')
    }

    if (language.literals.has(word)) {
      this.pending = null
      return this.add(start, end, 'literal')
    }

    if (this.pending !== null) {
      const pending = this.pending
      this.pending = null
      return this.add(start, end, pending)
    }

    if (language.calls) {
      if (end + 1 < line.length && line[end] === '!' && (line[end + 1] === '(' || line[end + 1] === '[')) return this.add(start, end + 1, 'function')
      if (after === '(') return this.add(start, end, 'function')
    }

    if (language.types.has(word)) return this.add(start, end, 'type')

    // A capitalized name before `{`, `=` or `;` is a property or member being declared or assigned, not a type.
    if (language.pascalTypes && isUpper(word[0]!) && [...word].some(isLower) && (start === 0 || line[start - 1] !== '.') && !'{=;'.includes(after || '\0'))
      return this.add(start, end, 'type')

    return end
  }

  private add(start: number, end: number, token: Token): number {
    this.spans.push([start, end - start, token])
    return end
  }

  private wordEnd(start: number): number {
    let end = start + 1
    while (end < this.line.length && (isWordChar(this.line[end]!) || this.language.wordChars.includes(this.line[end]!))) end++
    return end
  }

  private baseWordEnd(start: number): number {
    let end = start + 1
    while (end < this.line.length && isWordChar(this.line[end]!)) end++
    return end
  }

  private numberEnd(start: number): number {
    const line = this.line
    let end = start + 1
    while (end < line.length) {
      if (isAsciiLetterOrDigit(line[end]!) || line[end] === '_') end++
      else if (line[end] === '.' && end + 1 < line.length && isDigit(line[end + 1]!)) end++
      else break
    }
    return end
  }
}

/** CSS: the selector part of a line, up to `{`, or the whole line when it ends a selector list with `,`. */
function selectorEnd(line: string): number {
  const brace = line.indexOf('{')
  if (brace >= 0) return brace
  return line.trimEnd().endsWith(',') ? line.length : -1
}

function xml(language: Language, line: string, from: number, state: HighlightState, spans: Span[]): void {
  let index = from
  while (index < line.length) {
    if (state.embedded) {
      const embedded = state.embedded
      const close = indexOfIgnoreCase(line, '</' + embedded.closeTag, index)
      const end = close < 0 ? line.length : close
      if (end > index) scanLine(embedded.language, line.slice(0, end), index, embedded.state, spans)
      if (close < 0) return
      state.embedded = null
      index = close
      continue
    }

    if (state.block) {
      const open = state.block
      const [end, closed] = blockEnd(line, index, open)
      spans.push([index, end - index, open.token])
      if (!closed) return
      state.block = null
      index = end
      continue
    }

    index = state.inTag ? tagStep(language, line, index, state, spans) : textStep(language, line, index, state, spans)
  }
}

function textStep(language: Language, line: string, index: number, state: HighlightState, spans: Span[]): number {
  for (const block of language.blocks) {
    if (line.startsWith(block.open, index)) {
      const [end, closed] = blockEnd(line, index + block.open.length, block)
      spans.push([index, end - index, block.token])
      if (!closed) state.block = block
      return end
    }
  }

  const current = line[index]!
  if (current === '<' && index + 1 < line.length) {
    const closing = line[index + 1] === '/'
    const name = index + (closing ? 2 : 1)
    if (name < line.length && isWordStart(line[name]!)) {
      let end = name + 1
      while (end < line.length && (isWordChar(line[end]!) || language.wordChars.includes(line[end]!))) end++
      spans.push([name, end - name, 'tag'])
      state.inTag = true
      state.tagName = line.slice(name, end).toLowerCase()
      state.closingTag = closing
      state.lastAttribute = null
      state.langAttribute = null
      return end
    }
  }

  if (current === '&') {
    const semicolon = line.indexOf(';', index + 1)
    if (semicolon > index + 1 && semicolon - index <= 10 && [...line.slice(index + 1, semicolon)].every((character) => isAsciiLetterOrDigit(character) || character === '#')) {
      spans.push([index, semicolon + 1 - index, 'literal'])
      return semicolon + 1
    }
  }

  return index + 1
}

function tagStep(language: Language, line: string, index: number, state: HighlightState, spans: Span[]): number {
  const current = line[index]!
  if (current === '>') {
    state.inTag = false
    const selfClosing = index > 0 && line[index - 1] === '/'
    const tag = state.tagName
    const fallback = tag === null ? undefined : language.embedded[tag]
    if (!state.closingTag && !selfClosing && tag !== null && fallback !== undefined) {
      const embedded = (state.langAttribute !== null ? languageForAlias(state.langAttribute) : undefined) ?? languageForAlias(fallback)
      if (embedded) state.embedded = { language: embedded, state: new HighlightState(), closeTag: tag }
    }
    return index + 1
  }

  if (language.quotes.includes(current)) {
    const close = line.indexOf(current, index + 1)
    const end = close < 0 ? line.length : close + 1
    spans.push([index, end - index, 'string'])
    if (state.lastAttribute === 'lang') state.langAttribute = line.slice(index + 1, close < 0 ? line.length : close)
    state.lastAttribute = null
    return end
  }

  if (isWordStart(current)) {
    let end = index + 1
    while (end < line.length && (isWordChar(line[end]!) || language.wordChars.includes(line[end]!))) end++
    spans.push([index, end - index, 'property'])
    state.lastAttribute = line.slice(index, end).toLowerCase()
    return end
  }

  return index + 1
}

function markdown(line: string, state: HighlightState, spans: Span[]): void {
  const first = firstNonSpace(line)
  let end = line.length
  while (end > first && isSpace(line[end - 1]!)) end--
  const trimmed = line.slice(first, end)

  if (state.fence) {
    const fence = state.fence
    if (trimmed.startsWith(fence.marker)) {
      spans.push([first, end - first, 'meta'])
      state.fence = null
    } else if (fence.language) {
      scanLine(fence.language, line, 0, fence.state, spans)
    }
    return
  }

  if (trimmed.length === 0) return

  if (trimmed.startsWith('```') || trimmed.startsWith('~~~')) {
    let info = trimmed.slice(3).replace(/^[`~]+/, '').trim()
    const stop = info.search(/[ {]/)
    info = stop < 0 ? info : info.slice(0, stop)
    state.fence = { language: info.length === 0 ? undefined : languageForAlias(info), state: new HighlightState(), marker: trimmed.slice(0, 3) }
    spans.push([first, end - first, 'meta'])
    return
  }

  if (trimmed[0] === '#') {
    let level = 0
    while (level < trimmed.length && trimmed[level] === '#') level++
    if (level <= 6 && (level === trimmed.length || trimmed[level] === ' ')) {
      spans.push([first, end - first, 'keyword'])
      return
    }
  }

  if (trimmed[0] === '>') {
    spans.push([first, end - first, 'comment'])
    return
  }

  if (isRule(trimmed)) {
    spans.push([first, end - first, 'meta'])
    return
  }

  let index = first
  if (trimmed.length > 1 && '-*+'.includes(trimmed[0]!) && trimmed[1] === ' ') {
    spans.push([first, 1, 'meta'])
    index = first + 2
  } else {
    let digits = 0
    while (digits < trimmed.length && isDigit(trimmed[digits]!)) digits++
    if (digits > 0 && digits + 1 < trimmed.length && (trimmed[digits] === '.' || trimmed[digits] === ')') && trimmed[digits + 1] === ' ') {
      spans.push([first, digits + 1, 'meta'])
      index = first + digits + 2
    }
  }

  while (index < line.length) {
    if (line[index] === '`') {
      let run = 1
      while (index + run < line.length && line[index + run] === '`') run++
      const close = line.indexOf('`'.repeat(run), index + run)
      if (close < 0) {
        index += run
        continue
      }
      spans.push([index, close + run - index, 'string'])
      index = close + run
      continue
    }

    if (line[index] === ']' && index + 1 < line.length && line[index + 1] === '(') {
      const close = line.indexOf(')', index + 2)
      if (close >= 0) {
        spans.push([index + 2, close - index - 2, 'property'])
        index = close + 1
        continue
      }
    }

    index++
  }
}

/** A Markdown thematic break: three or more of the same `-`, `*` or `_`, spaces allowed. */
function isRule(trimmed: string): boolean {
  const marker = trimmed[0]!
  if (!'-*_'.includes(marker)) return false
  let count = 0
  for (const character of trimmed) {
    if (character === marker) count++
    else if (character !== ' ') return false
  }
  return count >= 3
}

function bracket(depth: number): Token {
  return depth % 3 === 0 ? 'bracket-1' : depth % 3 === 1 ? 'bracket-2' : 'bracket-3'
}

/** The index after the block's close marker, or the line length when it stays open. */
function blockEnd(line: string, from: number, block: Block): [end: number, closed: boolean] {
  const close = from <= line.length ? line.indexOf(block.close, from) : -1
  return close >= 0 ? [close + block.close.length, true] : [line.length, false]
}

/** The index after the closing quote, or the line length for an unterminated string. */
function stringEnd(line: string, quoteIndex: number, verbatim: boolean): number {
  const quote = line[quoteIndex]
  for (let index = quoteIndex + 1; index < line.length; index++) {
    if (!verbatim && line[index] === '\\') {
      index++
    } else if (line[index] === quote) {
      if (verbatim && index + 1 < line.length && line[index + 1] === quote) index++
      else return index + 1
    }
  }
  return line.length
}

function indexOfIgnoreCase(line: string, needle: string, from: number): number {
  const lower = needle.toLowerCase()
  for (let index = from; index + lower.length <= line.length; index++) {
    let match = true
    for (let offset = 0; offset < lower.length && match; offset++) match = line[index + offset]!.toLowerCase() === lower[offset]
    if (match) return index
  }
  return -1
}

function skipSpaces(line: string, index: number): number {
  while (index < line.length && isSpace(line[index]!)) index++
  return index
}

const firstNonSpace = (line: string) => skipSpaces(line, 0)
const isSpace = (character: string) => character === ' ' || character === '\t'
const isDigit = (character: string) => character >= '0' && character <= '9'
const isLetter = (character: string) => /\p{L}/u.test(character)
const isUpper = (character: string) => /\p{Lu}/u.test(character)
const isLower = (character: string) => /\p{Ll}/u.test(character)
const isWordStart = (character: string) => isLetter(character) || character === '_'
const isWordChar = (character: string) => isLetter(character) || isDigit(character) || character === '_'
const isAsciiLetterOrDigit = (character: string) => /[A-Za-z0-9]/.test(character)
const isHexDigit = (character: string) => /[0-9A-Fa-f]/.test(character)
