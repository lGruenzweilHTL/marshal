import type { ApiSession } from '../api/client'

/** Body of the unversioned `/health` endpoint (not part of the OpenAPI document). */
export interface HealthReport {
  status: 'healthy' | 'degraded' | 'unhealthy'
  checks: Record<string, HealthCheck>
}

export interface HealthCheck {
  status: 'healthy' | 'degraded' | 'unhealthy'
  description: string | null
  warnings: string[] | null
}

/** Reads `/health`; an unhealthy instance answers 503 with the same body. */
export async function loadHealth(session: ApiSession): Promise<HealthReport> {
  const response = await session.fetch('/health')
  return (await response.json()) as HealthReport
}
