import { describe, expect, it } from 'vitest'
import { diffStats } from './diffStats'

describe('diffStats', () => {
  it('counts files, added and removed lines of a git diff', () => {
    const diff = ['diff --git a/x.md b/x.md', '--- a/x.md', '+++ b/x.md', '@@ -1 +1,2 @@', '-old', '+new', '+more'].join('\n')
    expect(diffStats(diff)).toEqual({ files: 1, added: 2, removed: 1 })
  })

  it('counts files of a plain unified diff without diff header lines', () => {
    const diff = ['--- a/x.md', '+++ b/x.md', '@@ -1 +1 @@', '-old', '+new', '--- a/y.md', '+++ b/y.md', '@@ -1 +1 @@', '+y'].join('\n')
    expect(diffStats(diff)).toEqual({ files: 2, added: 2, removed: 1 })
  })
})
