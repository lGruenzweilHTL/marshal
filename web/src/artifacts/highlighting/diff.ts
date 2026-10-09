import { highlightLine, HighlightState } from './highlighter'
import { languageForFile, type Language, type Span } from './languages'

export type DiffRowKind = 'meta' | 'hunk' | 'added' | 'removed' | 'context'

/** One diff line: its kind and the highlight spans of the code after the marker (offset by the marker). */
export interface DiffRow {
  readonly kind: DiffRowKind
  readonly spans: Span[]
}

/**
 * Unified diffs with the code highlighted in each file's language (from `+++ b/path` or `diff --git`). The old side
 * (removed and context lines) and the new side (added and context lines) keep separate states. Port of the .NET
 * `DiffHighlighter`.
 */
export function highlightDiff(lines: readonly string[]): DiffRow[] {
  const rows: DiffRow[] = []
  let language: Language | undefined
  let oldSide = new HighlightState()
  let newSide = new HighlightState()
  let inHunk = false

  lines.forEach((line, index) => {
    const fileHeader = line.startsWith('diff ') || (line.startsWith('--- ') && index + 1 < lines.length && lines[index + 1]!.startsWith('+++ '))
    if (fileHeader || (!inHunk && !line.startsWith('@@'))) {
      inHunk = false
      const path = pathOf(line)
      if (path !== null) {
        language = languageForFile(path)
        oldSide = new HighlightState()
        newSide = new HighlightState()
      }
      rows.push({ kind: 'meta', spans: [] })
      return
    }

    if (line.startsWith('@@')) {
      inHunk = true
      rows.push({ kind: 'hunk', spans: [] })
      return
    }

    if (line.startsWith('\\')) {
      rows.push({ kind: 'meta', spans: [] })
      return
    }

    const code = line.slice(1)
    const kind: DiffRowKind = line.startsWith('+') ? 'added' : line.startsWith('-') ? 'removed' : 'context'
    let spans: Span[] = []
    if (language) {
      if (kind !== 'added') spans = highlightLine(language, code, oldSide)
      if (kind !== 'removed') spans = highlightLine(language, code, newSide)
    }
    rows.push({ kind, spans: spans.map(([start, length, token]) => [start + 1, length, token] as const) })
  })

  return rows
}

/** The new file's path from a `+++` header or a `diff --git` line; null otherwise. */
function pathOf(line: string): string | null {
  if (line.startsWith('+++ ')) {
    const path = line.slice(4).split('\t')[0]!.trim()
    if (path === '/dev/null') return null
    return path.startsWith('b/') ? path.slice(2) : path
  }
  if (line.startsWith('diff --git ')) {
    const separator = line.lastIndexOf(' b/')
    return separator < 0 ? null : line.slice(separator + 3)
  }
  return null
}
