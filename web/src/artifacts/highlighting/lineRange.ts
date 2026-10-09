/** A selected line range in the code viewer, 1-based and inclusive. */
export interface LineRange {
  readonly from: number
  readonly to: number
}

/** `#L12` or `#L12-L20` (either order); null for anything else or a range outside `lineCount`. */
export function parseLineHash(hash: string, lineCount: number): LineRange | null {
  const match = /^#L(\d+)(?:-L(\d+))?$/.exec(hash)
  if (!match) return null
  const a = Number(match[1])
  const b = match[2] === undefined ? a : Number(match[2])
  const range = { from: Math.min(a, b), to: Math.max(a, b) }
  return range.from >= 1 && range.to <= lineCount ? range : null
}

export function lineHash(range: LineRange): string {
  return range.from === range.to ? `#L${range.from}` : `#L${range.from}-L${range.to}`
}
