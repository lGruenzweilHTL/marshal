import { describe, expect, it } from 'vitest'
import { lineDiff } from './lineDiff'

describe('lineDiff', () => {
  it('marks only lines the other side does not have', () => {
    const { before, after } = lineDiff('a\nb\nc', 'a\nB\nc\nd')

    expect(before.map((line) => line.changed)).toEqual([false, true, false])
    expect(after.map((line) => line.changed)).toEqual([false, true, false, true])
  })

  it('marks nothing for equal texts', () => {
    const { before, after } = lineDiff('x\ny', 'x\ny')

    expect([...before, ...after].some((line) => line.changed)).toBe(false)
  })
})
