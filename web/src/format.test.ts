import { describe, expect, it } from 'vitest'
import { createFormatter } from './format'

// 2026-03-29 is the day Central Europe switches to summer time; 23:30 UTC is already the next day in Vienna.
const instant = '2026-03-29T23:30:00Z'
const now = new Date('2026-03-30T00:00:00Z')

describe('formatter', () => {
  const de = createFormatter('de', 'Europe/Vienna', () => now)
  const en = createFormatter('en', 'Europe/Vienna', () => now)

  it('formats dates and times in the time zone, not UTC', () => {
    expect(de.date(instant)).toBe('30.03.2026')
    expect(en.date(instant)).toBe('Mar 30, 2026')
    expect(de.time(instant)).toBe('01:30')
    expect(en.time(instant)).toBe('1:30 AM')
    expect(de.dateTime(instant)).toBe('30.03.2026, 01:30')
  })

  it('formats relative times', () => {
    expect(de.relative('2026-03-30T00:05:00Z')).toBe('in 5 Minuten')
    expect(en.relative('2026-03-29T22:00:00Z')).toBe('2 hours ago')
    expect(en.relative('2026-03-30T00:00:20Z')).toBe('now')
  })

  it('formats durations', () => {
    expect(en.duration(850)).toBe('850 ms')
    expect(en.duration(12_400)).toBe('12.4 s')
    expect(de.duration(12_400)).toBe('12,4 s')
    expect(en.duration(185_000)).toBe('3 min 5 s')
    expect(de.duration(7_380_000)).toBe('2 h 3 min')
  })

  it('formats numbers, percent and cost', () => {
    expect(de.number(1234567)).toBe('1.234.567')
    expect(en.number(1234567)).toBe('1,234,567')
    expect(de.percent(0.425)).toBe('42,5\u00a0%')
    expect(en.percent(0.425)).toBe('42.5%')
    expect(en.cost(0.5)).toBe('0.50')
    expect([en.bytes(512), en.bytes(1536), de.bytes(5 * 1024 * 1024 * 1024)]).toEqual(['512 B', '1.5 KB', '5 GB'])
    expect(de.cost(0.01234)).toBe('0,0123')
  })
})
