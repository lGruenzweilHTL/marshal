import { describe, expect, it } from 'vitest'
import { errorMessage, type Translator } from './errorMessage'

const catalog: Record<string, string> = {
  'errors.code.auth.https_required': 'Marshal muss über HTTPS geöffnet werden.',
  'errors.status.404': 'Nicht gefunden.',
  'errors.status.other': 'Anfrage fehlgeschlagen ({status}).',
  'errors.network': 'Marshal ist nicht erreichbar.',
  'errors.unexpected': 'Unerwarteter Fehler.',
}
const t: Translator = {
  te: (key) => key in catalog,
  t: (key, named) => (catalog[key] ?? key).replace(/\{(\w+)\}/g, (_, name: string) => String(named?.[name] ?? '')),
}

describe('errorMessage', () => {
  it('translates a known problem code instead of the server detail', () => {
    expect(errorMessage({ kind: 'problem', status: 400, code: 'auth.https_required', detail: 'Browser sessions require HTTPS.' }, t)).toBe('Marshal muss über HTTPS geöffnet werden.')
  })

  it('uses the server detail for an unknown code', () => {
    expect(errorMessage({ kind: 'problem', status: 409, code: 'task.busy', detail: 'The task is running.' }, t)).toBe('The task is running.')
  })

  it('falls back to the status message, then the generic one', () => {
    expect(errorMessage({ kind: 'problem', status: 404 }, t)).toBe('Nicht gefunden.')
    expect(errorMessage({ kind: 'problem', status: 418 }, t)).toBe('Anfrage fehlgeschlagen (418).')
  })

  it('has messages for network and unexpected failures', () => {
    expect(errorMessage({ kind: 'network' }, t)).toBe('Marshal ist nicht erreichbar.')
    expect(errorMessage({ kind: 'unexpected', cause: new Error('x') }, t)).toBe('Unerwarteter Fehler.')
  })
})
