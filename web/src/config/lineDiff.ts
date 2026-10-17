/** One line of a side-by-side diff; `changed` marks lines the other side does not have. */
export interface DiffLine {
  text: string
  changed: boolean
}

/** Line diff (longest common subsequence) of two texts, as the two sides of a side-by-side view. */
export function lineDiff(before: string, after: string): { before: DiffLine[]; after: DiffLine[] } {
  const a = before.split('\n')
  const b = after.split('\n')
  const lengths = Array.from({ length: a.length + 1 }, () => new Array<number>(b.length + 1).fill(0))
  for (let i = a.length - 1; i >= 0; i--)
    for (let j = b.length - 1; j >= 0; j--)
      lengths[i]![j] = a[i] === b[j] ? lengths[i + 1]![j + 1]! + 1 : Math.max(lengths[i + 1]![j]!, lengths[i]![j + 1]!)

  const keptA = new Set<number>()
  const keptB = new Set<number>()
  for (let i = 0, j = 0; i < a.length && j < b.length; ) {
    if (a[i] === b[j]) {
      keptA.add(i++)
      keptB.add(j++)
    } else if (lengths[i + 1]![j]! >= lengths[i]![j + 1]!) i++
    else j++
  }
  return {
    before: a.map((text, index) => ({ text, changed: !keptA.has(index) })),
    after: b.map((text, index) => ({ text, changed: !keptB.has(index) })),
  }
}
