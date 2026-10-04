/** Changed files and added / removed lines of a unified diff. */
export function diffStats(diff: string): { files: number; added: number; removed: number } {
  let files = 0
  let added = 0
  let removed = 0
  for (const line of diff.split('\n')) {
    if (line.startsWith('diff ')) files++
    else if (line.startsWith('+++') || line.startsWith('---')) continue
    else if (line.startsWith('+')) added++
    else if (line.startsWith('-')) removed++
  }
  return { files, added, removed }
}
