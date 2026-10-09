import { describe, expect, it } from 'vitest'
import { lineHash, parseLineHash } from './lineRange'

describe('line links', () => {
  it('parses single lines and ranges in either order', () => {
    expect(parseLineHash('#L4', 10)).toEqual({ from: 4, to: 4 })
    expect(parseLineHash('#L9-L2', 10)).toEqual({ from: 2, to: 9 })
  })

  it('ignores other fragments and ranges outside the file', () => {
    expect(parseLineHash('', 10)).toBeNull()
    expect(parseLineHash('#heading', 10)).toBeNull()
    expect(parseLineHash('#L0', 10)).toBeNull()
    expect(parseLineHash('#L3-L11', 10)).toBeNull()
  })

  it('writes the shortest hash', () => {
    expect(lineHash({ from: 5, to: 5 })).toBe('#L5')
    expect(lineHash({ from: 5, to: 9 })).toBe('#L5-L9')
  })
})
