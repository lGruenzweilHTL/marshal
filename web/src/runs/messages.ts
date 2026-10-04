import type { Catalog } from '../i18n/catalog'

export const en = {
  source: { voice: 'Voice', web: 'Web', task: 'Task', api: 'API' },
  outcome: { ok: 'ok', error: 'error', backgrounded: 'backgrounded', cancelled: 'cancelled' },
}

export const de: Catalog<typeof en> = {
  source: { voice: 'Sprache', web: 'Web', task: 'Aufgabe', api: 'API' },
  outcome: { ok: 'ok', error: 'Fehler', backgrounded: 'im Hintergrund', cancelled: 'abgebrochen' },
}
