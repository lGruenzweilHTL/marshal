/** Time left as "30 min" or "2 h 5 min". */
export function formatEta(seconds: number): string {
  const minutes = Math.max(0, Math.round(seconds / 60))
  return minutes < 90 ? `${minutes} min` : `${Math.floor(minutes / 60)} h ${minutes % 60} min`
}

export function formatPercent(progress: number): string {
  return `${Math.round(progress * 100)}%`
}

export function formatTemperature(heater: { temperature: number; target: number }): string {
  return `${Math.round(heater.temperature)} / ${Math.round(heater.target)} °C`
}
