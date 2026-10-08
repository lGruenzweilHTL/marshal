import type { Language } from './i18n/catalog'

/** Formats dates, numbers and durations for one interface language and time zone (`Intl` based). */
export interface Formatter {
  /** `30.03.2026` / `Mar 30, 2026`. */
  date(value: string | Date): string
  /** `01:30` / `1:30 AM`. */
  time(value: string | Date): string
  /** Date and time, short. */
  dateTime(value: string | Date): string
  /** `in 5 Minuten` / `2 hours ago`; under a minute is "now". */
  relative(value: string | Date): string
  /** `850 ms`, `12.4 s`, `3 min 5 s`, `2 h 3 min`. */
  duration(milliseconds: number): string
  number(value: number): string
  /** A fraction (`0.425`) as percent with up to one decimal. */
  percent(fraction: number): string
  /** A cost in the configured price unit, 2–4 decimals, no currency (the API has none). */
  cost(value: number): string
  /** A file size: `512 B`, `12.3 KB`, `4.5 MB`, `1.2 GB` (binary units). */
  bytes(value: number): string
}

const units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
]

export function createFormatter(language: Language, timeZone: string, now: () => Date = () => new Date()): Formatter {
  const date = new Intl.DateTimeFormat(language, { timeZone, dateStyle: 'medium' })
  const numericDate = new Intl.DateTimeFormat(language, { timeZone, day: '2-digit', month: '2-digit', year: 'numeric' })
  const time = new Intl.DateTimeFormat(language, { timeZone, hour: language === 'de' ? '2-digit' : 'numeric', minute: '2-digit' })
  const relative = new Intl.RelativeTimeFormat(language, { numeric: 'auto' })
  const number = new Intl.NumberFormat(language)
  const decimal = new Intl.NumberFormat(language, { maximumFractionDigits: 1 })
  const percent = new Intl.NumberFormat(language, { style: 'percent', maximumFractionDigits: 1 })
  const cost = new Intl.NumberFormat(language, { minimumFractionDigits: 2, maximumFractionDigits: 4 })
  const toDate = (value: string | Date) => (typeof value === 'string' ? new Date(value) : value)
  // German readers expect the numeric form (30.03.2026); English the short month (Mar 30, 2026).
  const formatDate = (value: string | Date) => (language === 'de' ? numericDate : date).format(toDate(value))

  return {
    date: formatDate,
    time: (value) => time.format(toDate(value)),
    dateTime: (value) => `${formatDate(value)}, ${time.format(toDate(value))}`,
    relative(value) {
      const seconds = (toDate(value).getTime() - now().getTime()) / 1000
      for (const [unit, size] of units) {
        if (Math.abs(seconds) >= size) return relative.format(Math.round(seconds / size), unit)
      }
      return relative.format(0, 'second')
    },
    duration(milliseconds) {
      if (milliseconds < 1000) return `${number.format(Math.round(milliseconds))} ms`
      const seconds = milliseconds / 1000
      if (seconds < 60) return `${decimal.format(seconds)} s`
      const minutes = Math.floor(seconds / 60)
      if (minutes < 60) return `${minutes} min ${Math.round(seconds % 60)} s`
      return `${Math.floor(minutes / 60)} h ${minutes % 60} min`
    },
    number: (value) => number.format(value),
    percent: (fraction) => percent.format(fraction),
    cost: (value) => cost.format(value),
    bytes(value) {
      const scale = (['B', 'KB', 'MB', 'GB', 'TB'] as const).findLastIndex((_, index) => value >= 1024 ** index)
      const unit = (['B', 'KB', 'MB', 'GB', 'TB'] as const)[Math.max(0, scale)]
      return `${scale <= 0 ? number.format(value) : decimal.format(value / 1024 ** scale)} ${unit}`
    },
  }
}

let current: Formatter = createFormatter('en', Intl.DateTimeFormat().resolvedOptions().timeZone)

/** Sets the formatter behind {@link formatCost} and {@link formatDateTime} (the locale store calls it). */
export function setCurrentFormatter(formatter: Formatter): void {
  current = formatter
}

/** A cost in the configured price unit, 2–4 decimals, in the current interface language. */
export function formatCost(value: number): string {
  return current.cost(value)
}

/** Date and time in the current interface language and time zone. */
export function formatDateTime(iso: string): string {
  return current.dateTime(iso)
}

/** API enum value in the spelling of the specs and logs (`escalatedByTaint` → `escalated_by_taint`). */
export function snakeCase(value: string): string {
  return value.replace(/[A-Z]/g, (letter) => `_${letter.toLowerCase()}`)
}
