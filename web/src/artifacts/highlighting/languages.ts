import definitions from './languages.json'

/** Highlight classes; the names are shared with the .NET highlighter and the `--syntax-*` theme tokens. */
export type Token =
  | 'keyword'
  | 'type'
  | 'function'
  | 'string'
  | 'number'
  | 'literal'
  | 'comment'
  | 'property'
  | 'meta'
  | 'tag'
  | 'variable'
  | 'bracket-1'
  | 'bracket-2'
  | 'bracket-3'
  | 'bracket-unmatched'

/** One highlighted range of a line: start, length, token. */
export type Span = readonly [start: number, length: number, token: Token]

export interface Block {
  readonly open: string
  readonly close: string
  readonly token: Token
}

export interface MetaLine {
  readonly prefix: string
  readonly suffix?: string
}

/** One language from `languages.json`; field meanings in the display-surfaces design, section 6. */
export interface Language {
  readonly id: string
  /** Display name, e.g. `C#`. */
  readonly name: string
  readonly mode: 'code' | 'xml' | 'css' | 'gcode' | 'markdown'
  readonly keywords: WordSet
  readonly types: WordSet
  readonly literals: WordSet
  readonly functionKeywords: WordSet
  readonly typeKeywords: WordSet
  readonly lineComments: readonly string[]
  readonly blocks: readonly Block[]
  readonly quotes: string
  readonly stringPrefixes: string
  readonly verbatimPrefixes: string
  readonly wordChars: string
  readonly keys?: 'json' | 'yaml' | 'ini'
  readonly metaLines: readonly MetaLine[]
  readonly embedded: Readonly<Record<string, string>>
  readonly loose: boolean
  readonly pascalTypes: boolean
  readonly calls: boolean
  readonly brackets: boolean
  readonly guides: boolean
  readonly annotations: boolean
  readonly variables: boolean
}

/** A word list, case-insensitive for languages with `ignoreCase`. */
export class WordSet {
  private readonly words: Set<string>

  constructor(
    words: string | undefined,
    private readonly ignoreCase: boolean,
  ) {
    this.words = new Set((words ?? '').split(' ').filter((word) => word.length > 0).map((word) => (ignoreCase ? word.toLowerCase() : word)))
  }

  has(word: string): boolean {
    return this.words.has(this.ignoreCase ? word.toLowerCase() : word)
  }
}

interface Entry {
  id: string
  name?: string
  mode?: string
  extensions?: string[]
  names?: string[]
  aliases?: string[]
  keywords?: string
  types?: string
  literals?: string
  functionKeywords?: string
  typeKeywords?: string
  ignoreCase?: boolean
  lineComments?: string[]
  blocks?: { open: string; close: string; token: string }[]
  quotes?: string
  stringPrefixes?: string
  verbatimPrefixes?: string
  wordChars?: string
  loose?: boolean
  pascalTypes?: boolean
  calls?: boolean
  brackets?: boolean
  guides?: boolean
  annotations?: boolean
  variables?: boolean
  keys?: string
  metaLines?: { prefix: string; suffix?: string }[]
  embedded?: Record<string, string>
}

const byId = new Map<string, Language>()
const byExtension = new Map<string, Language>()
const byName = new Map<string, Language>()
const byAlias = new Map<string, Language>()

for (const entry of definitions.languages as Entry[]) {
  const ignoreCase = entry.ignoreCase ?? false
  const language: Language = {
    id: entry.id,
    name: entry.name ?? entry.id,
    mode: (entry.mode ?? 'code') as Language['mode'],
    keywords: new WordSet(entry.keywords, ignoreCase),
    types: new WordSet(entry.types, ignoreCase),
    literals: new WordSet(entry.literals, ignoreCase),
    functionKeywords: new WordSet(entry.functionKeywords, ignoreCase),
    typeKeywords: new WordSet(entry.typeKeywords, ignoreCase),
    lineComments: entry.lineComments ?? [],
    blocks: (entry.blocks ?? []).map((block) => ({ ...block, token: block.token as Token })),
    quotes: entry.quotes ?? '',
    stringPrefixes: entry.stringPrefixes ?? '',
    verbatimPrefixes: entry.verbatimPrefixes ?? '',
    wordChars: entry.wordChars ?? '',
    keys: entry.keys as Language['keys'],
    metaLines: entry.metaLines ?? [],
    embedded: entry.embedded ?? {},
    loose: entry.loose ?? false,
    pascalTypes: entry.pascalTypes ?? false,
    calls: entry.calls ?? true,
    brackets: entry.brackets ?? true,
    guides: entry.guides ?? true,
    annotations: entry.annotations ?? false,
    variables: entry.variables ?? false,
  }
  byId.set(language.id, language)
  for (const extension of entry.extensions ?? []) byExtension.set(extension, language)
  for (const name of entry.names ?? []) byName.set(name, language)
  for (const alias of entry.aliases ?? []) byAlias.set(alias, language)
}

export const languageCount = byId.size

/** The language of a file (exact name like `Dockerfile`, else the extension), or undefined for plain text. */
export function languageForFile(name: string): Language | undefined {
  const lower = name.toLowerCase()
  const file = lower.slice(lower.lastIndexOf('/') + 1)
  const named = byName.get(file)
  if (named) return named
  const dot = file.lastIndexOf('.')
  return dot < 0 ? undefined : byExtension.get(file.slice(dot + 1))
}

/** A language by id, alias or extension (Markdown fence info, `lang` attributes). */
export function languageForAlias(alias: string): Language | undefined {
  const key = alias.trim().toLowerCase()
  return byId.get(key) ?? byAlias.get(key) ?? byExtension.get(key)
}
