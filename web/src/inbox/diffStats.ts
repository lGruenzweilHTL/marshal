/** Changed files and added / removed lines of a unified diff; each file has one `+++` header, with or without `diff --git`. */
export function diffStats(diff: string): { files: number; added: number; removed: number } {
  let files = 0
  let added = 0
  let removed = 0
  for (const line of diff.split('\n')) {
    if (line.startsWith('+++')) files++
    else if (line.startsWith('---')) continue
    else if (line.startsWith('+')) added++
    else if (line.startsWith('-')) removed++
  }
  return { files, added, removed }
}
