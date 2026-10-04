import { supportedLanguage, type Language } from '../i18n/catalog'
import { scenarios, type ScenarioName } from './scenarios'

const storageKey = 'marshal.preview'

export interface PreviewSettings {
  scenario: ScenarioName
  language: Language
}

/** Scenario and language from the URL (`?scenario=failing&lang=de`, linkable), else from this tab's last choice. */
export function readSettings(): PreviewSettings {
  const query = new URLSearchParams(location.search)
  let stored: Partial<PreviewSettings> = {}
  try {
    stored = JSON.parse(sessionStorage.getItem(storageKey) ?? '{}') as Partial<PreviewSettings>
  } catch {
    stored = {}
  }
  const scenario = query.get('scenario') ?? stored.scenario
  return {
    scenario: (scenarios as readonly string[]).includes(scenario ?? '') ? (scenario as ScenarioName) : 'normal',
    language: supportedLanguage(query.get('lang') ?? stored.language) ?? 'en',
  }
}

export function storeSettings(settings: PreviewSettings): void {
  try {
    sessionStorage.setItem(storageKey, JSON.stringify(settings))
  } catch {
    // Storage blocked: the URL still carries the settings.
  }
  const url = new URL(location.href)
  url.searchParams.set('scenario', settings.scenario)
  url.searchParams.set('lang', settings.language)
  history.replaceState(history.state, '', url)
}
