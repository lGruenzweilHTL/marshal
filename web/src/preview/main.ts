// Preview mode entry (`npm run dev:preview`): the full app against the built-in fake API, signed in, no .NET host.
import { startApp } from '../bootstrap'
import { FakeLiveEvents } from '../testing/fakeLiveEvents'
import PreviewRoot from './PreviewRoot.vue'
import { createPreviewServer } from './scenarios'
import { readSettings, storeSettings } from './settings'

const settings = readSettings()
storeSettings(settings)
const server = createPreviewServer(settings.scenario, settings.language)
// A little latency, so loading states are visible.
const fetch: typeof globalThis.fetch = async (input, init) => {
  await new Promise((resolve) => setTimeout(resolve, 250 + Math.random() * 350))
  return server.fetch(input, init)
}

startApp({
  fetch,
  live: () => new FakeLiveEvents(),
  browser: { languages: [settings.language], timeZone: 'Europe/Vienna' },
  root: PreviewRoot,
})
