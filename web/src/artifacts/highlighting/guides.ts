/** Indent guides: the file's indent unit and the number of guides per line (spec code-highlighting). */
export function indentGuides(lines: readonly string[]): { unit: number; guides: number[] } {
  const indents = lines.map(indent)

  const steps = new Map<number, number>()
  let previous: number | null = null
  for (const current of indents) {
    if (current === null) continue
    if (previous !== null && current > previous) steps.set(current - previous, (steps.get(current - previous) ?? 0) + 1)
    previous = current
  }

  let unit = 4
  if (steps.size > 0) {
    const [best] = [...steps].sort((a, b) => b[1] - a[1] || a[0] - b[0])
    unit = Math.min(8, Math.max(2, best![0]))
  }
  const guides = indents.map((value) => (value === null ? -1 : Math.ceil(value / unit)))

  for (let index = 0; index < guides.length; index++) {
    if (guides[index]! >= 0) continue
    let before = index - 1
    while (before >= 0 && indents[before] === null) before--
    let after = index + 1
    while (after < guides.length && indents[after] === null) after++
    guides[index] = Math.min(before >= 0 ? guides[before]! : 0, after < guides.length ? guides[after]! : 0)
  }

  return { unit, guides }
}

/** Leading spaces (a tab counts four), or null for a blank line. */
function indent(line: string): number | null {
  let width = 0
  for (const character of line) {
    if (character === ' ') width++
    else if (character === '\t') width += 4
    else return width
  }
  return null
}
