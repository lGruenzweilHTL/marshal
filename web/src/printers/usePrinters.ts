import { computed, shallowRef } from 'vue'
import type { Schemas } from '../api/client'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useLiveEvent } from '../live/liveEvents'

/** The configured printers, kept current by the live `printer.state` events (null state: the printer went offline). */
export function usePrinters() {
  const api = useApi()
  const live = shallowRef<Record<string, Schemas['PrinterState'] | null>>({})
  const load = useLoad(() => {
    live.value = {}
    return api.call((client) => client.GET('/api/v1/printers'))
  })

  useLiveEvent('printer', 'printer.state', ({ printer, state }) => {
    live.value = { ...live.value, [printer]: state }
  })

  const printers = computed(() =>
    (load.data.value ?? []).map((printer) =>
      printer.name in live.value ? { ...printer, online: live.value[printer.name] !== null, state: live.value[printer.name] ?? null } : printer,
    ),
  )
  return { load, printers }
}
