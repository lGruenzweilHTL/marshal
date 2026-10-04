import { describe, expect, it } from 'vitest'
import { createAppI18n } from './index'
import { createLocale } from './locale'
import { createApiSession } from '../api/client'
import { FakeServer } from '../testing/fakeServer'

const prefs = (language: string, timeZone: string) => ({ language, timeZone, quietHours: [], notificationTargets: { person: null, pushServices: [] }, doNotDisturbUntil: null })

function setup(browser: readonly string[], server = new FakeServer()) {
  const i18n = createAppI18n()
  const api = createApiSession({ baseUrl: 'https://marshal.test', fetch: server.fetch })
  return { i18n, api, locale: createLocale(i18n, { languages: browser, timeZone: 'America/New_York' }) }
}

describe('locale store', () => {
  it('follows the browser language before sign-in', () => {
    const { i18n, locale } = setup(['de-AT', 'en'])

    expect(locale.language.value).toBe('de')
    expect(i18n.global.locale.value).toBe('de')
    expect(locale.timeZone.value).toBe('America/New_York')
  })

  it('falls back to English for an unsupported browser language', () => {
    const { locale } = setup(['fr-FR', 'it'])

    expect(locale.language.value).toBe('en')
  })

  it('takes the first supported browser language', () => {
    expect(setup(['fr', 'de']).locale.language.value).toBe('de')
  })

  it('uses the preferences after sign-in', async () => {
    const server = new FakeServer().on('GET', '/api/v1/me/preferences', 200, prefs('de', 'Europe/Vienna'))
    const { api, i18n, locale } = setup(['en-US'], server)

    await locale.loadPreferences(api)

    expect(locale.language.value).toBe('de')
    expect(i18n.global.locale.value).toBe('de')
    expect(locale.timeZone.value).toBe('Europe/Vienna')
  })

  it('keeps the browser language when the preferences cannot be loaded', async () => {
    const server = new FakeServer().on('GET', '/api/v1/me/preferences', 500)
    const { api, locale } = setup(['de'], server)

    const result = await locale.loadPreferences(api)

    expect(result.error?.kind).toBe('problem')
    expect(locale.language.value).toBe('de')
  })

  it('switches without a reload after the preferences are saved', () => {
    const { i18n, locale } = setup(['en'])
    expect(i18n.global.t('actions.save')).toBe('Save')

    locale.apply(prefs('de', 'Europe/Berlin'))

    expect(i18n.global.t('actions.save')).toBe('Speichern')
    expect(locale.timeZone.value).toBe('Europe/Berlin')
  })

  it('formats with the current language and time zone', () => {
    const { locale } = setup(['en'])
    locale.apply(prefs('de', 'Europe/Vienna'))

    expect(locale.format.value.number(1234.5)).toBe('1.234,5')
  })
})
