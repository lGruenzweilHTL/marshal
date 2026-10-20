import { shallowRef } from 'vue'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'

export interface PendingControl {
  description: string
  token: string
}

/**
 * Sends printer controls. A confirm-tier control comes back with a token; it waits in `pending` until the user
 * confirms (a second request with the token) or dismisses it. Errors stay in `error` until the next control. With
 * `widget`, both requests carry the widget flag, so the server records them as a `widget:printer` run.
 */
export function usePrinterControls(printer: () => string, done?: () => void, widget = false) {
  const api = useApi()
  const pending = shallowRef<PendingControl | null>(null)
  const path = () => ({ path: { printer: printer() } })

  const send = useSubmit(async (body: Schemas['PrinterControlRequest']) => {
    const result = await api.call((client) => client.POST('/api/v1/printers/{printer}/controls', { params: path(), body: widget ? { ...body, widget: true } : body }))
    if (result.data?.status === 'confirmation_required' && result.data.token) pending.value = { description: result.data.description, token: result.data.token }
    else if (result.data) done?.()
    return result
  })

  const confirmation = useSubmit(async () => {
    const token = pending.value?.token ?? ''
    const result = await api.call((client) => client.POST('/api/v1/printers/{printer}/controls/confirm', { params: path(), body: widget ? { token, widget: true } : { token } }))
    pending.value = null
    if (result.data) done?.()
    return result
  })

  return {
    pending,
    busy: send.busy,
    error: send.error,
    confirmError: confirmation.error,
    confirming: confirmation.busy,
    send: (body: Schemas['PrinterControlRequest']) => send.submit(body),
    confirm: () => confirmation.submit(),
    dismiss: () => (pending.value = null),
  }
}

export type PrinterControls = ReturnType<typeof usePrinterControls>
