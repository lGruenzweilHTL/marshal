import { describe, expect, it } from 'vitest'
import cases from './cases.json'
import { highlightDiff } from './diff'
import { indentGuides } from './guides'
import { highlight } from './highlighter'
import { languageCount, languageForAlias, languageForFile } from './languages'

// The parity cases shared with the .NET highlighter (Marshal.Artifacts.Tests/Highlighting/CodeHighlighterTests).
const format = (spans: readonly (readonly [number, number, string])[]) => spans.map(([start, length, token]) => `[${start},${length},${token}]`).join(' ')

describe('highlighter (shared cases)', () => {
  it('loads every language', () => {
    expect(languageCount).toBeGreaterThanOrEqual(25)
  })

  it('picks the language by file name', () => {
    for (const [file, id] of cases.languages) expect(languageForFile(file!)?.id ?? null).toBe(id)
  })

  it('picks the language by alias', () => {
    for (const [alias, id] of cases.aliases) expect(languageForAlias(alias!)?.id ?? null).toBe(id)
  })

  it.each(cases.highlight.map((test) => [test.name, test] as const))('%s', (_name, test) => {
    const language = languageForFile(test.file)
    expect(language).toBeDefined()
    const actual = highlight(language!, test.lines).map(format)
    expect(actual).toEqual(test.spans.map((spans) => format(spans as [number, number, string][])))
  })

  it.each(cases.guides.map((test) => [test.name, test] as const))('guides: %s', (_name, test) => {
    expect(indentGuides(test.lines)).toEqual({ unit: test.unit, guides: test.guides })
  })

  it.each(cases.diff.map((test) => [test.name, test] as const))('diff: %s', (_name, test) => {
    const actual = highlightDiff(test.lines).map((row) => `${row.kind} ${format(row.spans)}`)
    expect(actual).toEqual(test.rows.map((row) => `${row.kind} ${format(row.spans as [number, number, string][])}`))
  })
})
