/**
 * The host side of the MCP Apps messaging protocol (2026-01-26): JSON-RPC 2.0 over `postMessage` between the chat
 * and a sandboxed app frame. Only the method names Marshal answers are listed; everything else is "method not found".
 */
export const protocolVersion = '2026-01-26'

export const methodNotFound = -32601
export const callFailed = -32000

export interface RpcRequest {
  jsonrpc: '2.0'
  id: string | number
  method: string
  params?: Record<string, unknown>
}

export interface RpcNotification {
  jsonrpc: '2.0'
  method: string
  params?: Record<string, unknown>
}

/** A JSON-RPC request (has an id) or notification from the app; null for anything else. */
export function parseMessage(data: unknown): RpcRequest | RpcNotification | null {
  if (typeof data !== 'object' || data === null) return null
  const message = data as Record<string, unknown>
  if (message.jsonrpc !== '2.0' || typeof message.method !== 'string') return null
  if (message.params !== undefined && (typeof message.params !== 'object' || message.params === null)) return null
  return message as unknown as RpcRequest | RpcNotification
}

export function isRequest(message: RpcRequest | RpcNotification): message is RpcRequest {
  return 'id' in message && (typeof message.id === 'string' || typeof message.id === 'number')
}

/** Only web links are shown; the user opens them, the host never navigates. */
export function safeLink(url: unknown): string | null {
  if (typeof url !== 'string') return null
  try {
    const parsed = new URL(url)
    return parsed.protocol === 'https:' || parsed.protocol === 'http:' ? parsed.href : null
  } catch {
    return null
  }
}
