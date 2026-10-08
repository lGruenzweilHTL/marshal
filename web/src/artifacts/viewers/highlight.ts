import hljs from 'highlight.js/lib/core'
import bash from 'highlight.js/lib/languages/bash'
import c from 'highlight.js/lib/languages/c'
import cpp from 'highlight.js/lib/languages/cpp'
import csharp from 'highlight.js/lib/languages/csharp'
import css from 'highlight.js/lib/languages/css'
import dockerfile from 'highlight.js/lib/languages/dockerfile'
import gcode from 'highlight.js/lib/languages/gcode'
import go from 'highlight.js/lib/languages/go'
import ini from 'highlight.js/lib/languages/ini'
import java from 'highlight.js/lib/languages/java'
import javascript from 'highlight.js/lib/languages/javascript'
import json from 'highlight.js/lib/languages/json'
import kotlin from 'highlight.js/lib/languages/kotlin'
import php from 'highlight.js/lib/languages/php'
import plaintext from 'highlight.js/lib/languages/plaintext'
import powershell from 'highlight.js/lib/languages/powershell'
import python from 'highlight.js/lib/languages/python'
import ruby from 'highlight.js/lib/languages/ruby'
import rust from 'highlight.js/lib/languages/rust'
import scss from 'highlight.js/lib/languages/scss'
import sql from 'highlight.js/lib/languages/sql'
import swift from 'highlight.js/lib/languages/swift'
import typescript from 'highlight.js/lib/languages/typescript'
import xml from 'highlight.js/lib/languages/xml'
import yaml from 'highlight.js/lib/languages/yaml'

// Only this module imports highlight.js; the code viewer loads it lazily, so it stays out of the main chunk.
const languages = { bash, c, cpp, csharp, css, dockerfile, gcode, go, ini, java, javascript, json, kotlin, php, plaintext, powershell, python, ruby, rust, scss, sql, swift, typescript, xml, yaml }
for (const [name, language] of Object.entries(languages)) hljs.registerLanguage(name, language)

/** Extension (the server's code extensions) → registered language. */
const byExtension: Record<string, string> = {
  cs: 'csharp', csproj: 'xml', props: 'xml', ts: 'typescript', tsx: 'typescript', js: 'javascript', jsx: 'javascript', mjs: 'javascript', cjs: 'javascript',
  vue: 'xml', py: 'python', scad: 'c', yaml: 'yaml', yml: 'yaml', json: 'json', toml: 'ini', ini: 'ini', cfg: 'ini', conf: 'ini', sh: 'bash', bash: 'bash',
  ps1: 'powershell', c: 'c', h: 'c', cpp: 'cpp', hpp: 'cpp', rs: 'rust', go: 'go', java: 'java', kt: 'kotlin', swift: 'swift', rb: 'ruby', php: 'php',
  sql: 'sql', css: 'css', scss: 'scss', xml: 'xml', gcode: 'gcode', dockerfile: 'dockerfile',
}

export function languageOf(name: string): string {
  const file = name.toLowerCase()
  const extension = file === 'dockerfile' ? 'dockerfile' : file.includes('.') ? file.slice(file.lastIndexOf('.') + 1) : ''
  return byExtension[extension] ?? 'plaintext'
}

/** Highlighted HTML per line (highlight.js escapes the text; spans never cross a line break after the split). */
export function highlightLines(code: string, language: string): string[] {
  const html = hljs.highlight(code.replace(/\n$/, ''), { language, ignoreIllegals: true }).value
  const lines: string[] = []
  let open: string[] = []
  for (const line of html.split('\n')) {
    const prefix = open.join('')
    for (const match of line.matchAll(/<span[^>]*>|<\/span>/g)) {
      if (match[0] === '</span>') open.pop()
      else open.push(match[0])
    }
    lines.push(prefix + line + '</span>'.repeat(open.length))
  }
  return lines
}
