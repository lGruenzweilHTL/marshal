import type { ApiSession } from '../api/client'
import { problemMessage } from '../api/problem'

/** Approves or rejects a background approval; returns why it failed, or null. */
export async function decideApproval(session: ApiSession, id: string, approve: boolean): Promise<string | null> {
  const path = { params: { path: { id } } }
  const { data, error } = approve
    ? await session.api.POST('/api/v1/approvals/{id}/approve', path)
    : await session.api.POST('/api/v1/approvals/{id}/reject', path)
  return data ? null : problemMessage(error, approve ? 'The approval could not be approved.' : 'The approval could not be rejected.')
}

/** Merges or rejects a coding proposal; returns why it failed, or null. */
export async function decideProposal(session: ApiSession, id: string, merge: boolean): Promise<string | null> {
  const path = { params: { path: { id } } }
  const { data, error } = merge
    ? await session.api.POST('/api/v1/proposals/{id}/merge', path)
    : await session.api.POST('/api/v1/proposals/{id}/reject', path)
  return data ? null : problemMessage(error, merge ? 'The change could not be merged.' : 'The change could not be rejected.')
}
