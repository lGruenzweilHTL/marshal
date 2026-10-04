import type { Api, ApiResult } from '../api/client'

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

/** Reads `/health`; an unhealthy instance answers 503 with the same body, so 503 is data, not a failure. */
export async function loadHealth(api: Api): Promise<ApiResult<HealthReport>> {
  const result = await api.fetch('/health')
  if (result.error) return result
  const response = result.data
  if (!response.ok && response.status !== 503) return { error: { kind: 'problem', status: response.status } }
  try {
    return { data: (await response.json()) as HealthReport }
  } catch (cause) {
    return { error: { kind: 'unexpected', cause } }
  }
}
