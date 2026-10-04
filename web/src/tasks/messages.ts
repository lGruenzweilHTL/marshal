import type { Catalog } from '../i18n/catalog'

export const en = {
  status: { pending: 'pending', running: 'running', waiting: 'waiting', done: 'done', failed: 'failed', cancelled: 'cancelled' },
  killSwitch: {
    title: 'Background work',
    running: '{count} tasks running | {count} task running | {count} tasks running',
    stop: 'Stop all',
    stoppedTitle: 'All background work stopped',
    stoppedBody: 'Tasks stay stopped until you resume them.',
    cancelled: '{count} cancelled',
    resume: 'Resume',
    confirmTitle: 'Stop all background work?',
    confirmBody: 'Running tasks are cancelled and no task starts until you resume.',
    confirm: 'Stop all',
  },
}

export const de: Catalog<typeof en> = {
  status: { pending: 'wartet auf Start', running: 'läuft', waiting: 'wartet', done: 'fertig', failed: 'fehlgeschlagen', cancelled: 'abgebrochen' },
  killSwitch: {
    title: 'Hintergrundarbeit',
    running: '{count} Aufgaben laufen | {count} Aufgabe läuft | {count} Aufgaben laufen',
    stop: 'Alle stoppen',
    stoppedTitle: 'Alle Hintergrundarbeit gestoppt',
    stoppedBody: 'Aufgaben bleiben gestoppt, bis du sie fortsetzt.',
    cancelled: '{count} abgebrochen',
    resume: 'Fortsetzen',
    confirmTitle: 'Alle Hintergrundarbeit stoppen?',
    confirmBody: 'Laufende Aufgaben werden abgebrochen, und keine Aufgabe startet, bis du fortsetzt.',
    confirm: 'Alle stoppen',
  },
}
