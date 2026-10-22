const pad = (value: number) => String(value).padStart(2, '0')

/** ISO instant to a `datetime-local` value in the browser's zone; empty for none. */
export function toLocalInput(iso: string | null | undefined): string {
  if (!iso) return ''
  const date = new Date(iso)
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

/** A `datetime-local` value to an ISO instant; null for an empty field. */
export function fromLocalInput(value: string): string | null {
  return value ? new Date(value).toISOString() : null
}

/** A chunk is closed once its valid time has ended. */
export function isClosed(validTo: string | null | undefined, now: Date = new Date()): boolean {
  return validTo != null && new Date(validTo).getTime() <= now.getTime()
}
