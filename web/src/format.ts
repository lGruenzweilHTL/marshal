const cost = new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 4 })
const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' })

/** A cost in the configured price unit, 2–4 decimals. */
export function formatCost(value: number): string {
  return cost.format(value)
}

export function formatDateTime(iso: string): string {
  return dateTime.format(new Date(iso))
}

/** API enum value in the spelling of the specs and logs (`escalatedByTaint` → `escalated_by_taint`). */
export function snakeCase(value: string): string {
  return value.replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`)
}
