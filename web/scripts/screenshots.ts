/**
 * Screenshot sweep (`npm run screenshots`): opens every route of preview mode at phone and desktop width, in light and
 * dark, and writes one PNG per route and variant to `web/.screenshots/` for a visual review. Layout checks that jsdom
 * cannot measure (vertical centres of controls in one row) run on the same pages and fail the sweep.
 *
 * Options: `--scenario normal,empty,failing` (default: all three), `--lang en|de`, `--only <route-name>,...`,
 * `--no-shots` (checks only). The browser is never downloaded: `PLAYWRIGHT_CHROMIUM`, `/opt/pw-browsers/chromium` or
 * the newest Chromium in the Playwright cache is used.
 */
import { existsSync, mkdirSync, readdirSync } from 'node:fs'
import { homedir } from 'node:os'
import { join, resolve } from 'node:path'
import { chromium, type Page } from 'playwright'
import { createServer } from 'vite'
import { ids } from '../src/preview/scenarios/normal.ts'

interface Route {
  name: string
  path: string
}

/** Every page of the app shell; parameterized routes use ids from the preview fixtures. */
const routes: Route[] = [
  { name: 'home', path: '/' },
  { name: 'chat', path: '/chat' },
  { name: 'chat-web', path: `/chat/${ids.conversation(2)}` },
  { name: 'chat-voice', path: `/chat/${ids.conversation(3)}` },
  { name: 'chat-history', path: '/chat/history' },
  { name: 'inbox', path: '/inbox' },
  { name: 'approval', path: `/inbox/approvals/${ids.approvalPrint}` },
  { name: 'proposal', path: `/inbox/proposals/${ids.proposal}` },
  { name: 'notification', path: `/inbox/notifications/${ids.notification(1)}` },
  { name: 'printers', path: '/printers' },
  { name: 'tasks', path: '/tasks' },
  { name: 'task', path: `/tasks/${ids.printTask}` },
  { name: 'runs', path: '/runs' },
  { name: 'run', path: `/runs/${ids.run(6)}` },
  { name: 'run-voice', path: `/runs/${ids.run(1)}` },
  { name: 'artifacts', path: '/artifacts' },
  { name: 'cost', path: '/cost' },
  { name: 'memory', path: '/memory' },
  { name: 'memory-chunk', path: `/memory/chunks/${ids.chunk(2)}` },
  { name: 'memory-projects', path: '/memory/projects' },
  { name: 'memory-map', path: '/memory/map' },
  { name: 'memory-audit', path: '/memory/audit' },
  { name: 'mail-accounts', path: '/mail/accounts' },
  { name: 'preferences', path: '/preferences' },
  { name: 'security', path: '/security' },
  { name: 'secrets', path: '/security/secrets' },
  { name: 'system', path: '/system' },
  { name: 'config', path: '/system/config' },
  { name: 'not-found', path: '/does-not-exist' },
]

const widths = { phone: 375, desktop: 1280 } as const
const themes = ['light', 'dark'] as const

/** A layout rule checked in the real browser: returns a message per violation. */
interface Check {
  route: string
  name: string
  run: (page: Page) => Promise<string[]>
}

/** Controls side by side in one row share a vertical centre (web-shell design system rule). */
const checks: Check[] = [
  {
    route: 'chat',
    name: 'composer: one-line draft centred on the send button',
    run: async (page) => {
      const field = await page.locator('.composer textarea').boundingBox()
      const button = await page.locator('.composer button[type=submit]').boundingBox()
      if (!field || !button) return ['composer not found']
      const delta = Math.abs(field.y + field.height / 2 - (button.y + button.height / 2))
      return delta > 1 ? [`textarea centre is ${delta.toFixed(1)} px away from the button centre`] : []
    },
  },
  {
    route: 'chat',
    name: 'composer: multi-line draft keeps the button at the bottom',
    run: async (page) => {
      await page.locator('.composer textarea').fill('one\ntwo\nthree\nfour')
      const field = await page.locator('.composer textarea').boundingBox()
      const button = await page.locator('.composer button[type=submit]').boundingBox()
      await page.locator('.composer textarea').fill('')
      if (!field || !button) return ['composer not found']
      const delta = Math.abs(field.y + field.height - (button.y + button.height))
      return delta > 1 ? [`button bottom is ${delta.toFixed(1)} px away from the textarea bottom`] : []
    },
  },
]

function option(name: string): string | undefined {
  const index = process.argv.indexOf(`--${name}`)
  return index >= 0 ? process.argv[index + 1] : undefined
}

function browserPath(): string | undefined {
  if (process.env.PLAYWRIGHT_CHROMIUM) return process.env.PLAYWRIGHT_CHROMIUM
  if (existsSync('/opt/pw-browsers/chromium')) return '/opt/pw-browsers/chromium'
  const cache = join(homedir(), '.cache', 'ms-playwright')
  if (!existsSync(cache)) return undefined
  const newest = readdirSync(cache).filter((entry) => /^chromium-\d+$/.test(entry)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)))[0]
  const executable = newest && join(cache, newest, 'chrome-linux64', 'chrome')
  return executable && existsSync(executable) ? executable : undefined
}

const scenarios = (option('scenario') ?? 'normal,empty,failing').split(',')
const language = option('lang') ?? 'en'
const only = option('only')?.split(',')
const shots = !process.argv.includes('--no-shots')
const output = resolve(import.meta.dirname, '..', '.screenshots')

const server = await createServer({ mode: 'preview', root: resolve(import.meta.dirname, '..'), server: { port: 0, strictPort: false }, logLevel: 'warn' })
await server.listen()
const base = server.resolvedUrls?.local[0]?.replace(/\/$/, '')
if (!base) throw new Error('The preview server did not report a URL.')

const browser = await chromium.launch({ executablePath: browserPath() })
const failures: string[] = []
try {
  for (const scenario of scenarios) {
    for (const [layout, width] of Object.entries(widths)) {
      for (const theme of themes) {
        const context = await browser.newContext({ viewport: { width, height: layout === 'phone' ? 812 : 900 }, deviceScaleFactor: layout === 'phone' ? 2 : 1, colorScheme: theme })
        await context.addInitScript((value) => {
          try {
            localStorage.setItem('marshal.appearance', value)
          } catch {
            // Storage blocked: the color scheme still follows the context.
          }
        }, theme)
        const page = await context.newPage()
        const folder = join(output, `${scenario}-${language}`, `${layout}-${theme}`)
        mkdirSync(folder, { recursive: true })
        for (const route of routes.filter((candidate) => !only || only.includes(candidate.name))) {
          await page.goto(`${base}${route.path}?scenario=${scenario}&lang=${language}`)
          // The dev server compiles modules on first use: wait for the routed page, then for its requests to settle.
          try {
            await page.locator('.main .page').first().waitFor({ state: 'visible', timeout: 30_000 })
          } catch {
            failures.push(`${route.name} (${scenario}, ${layout}, ${theme}): the page did not render within 30 s`)
          }
          await page.waitForLoadState('networkidle')
          // Preview responses arrive after a random delay; wait until no section shows its loading skeleton.
          await page.waitForFunction(() => !document.querySelector('.load-state[aria-busy="true"]'), undefined, { timeout: 15_000 }).catch(() => {
            failures.push(`${route.name} (${scenario}, ${layout}, ${theme}): still loading after 15 s`)
          })
          // Sections without a skeleton (health banner, passkeys) answer within one more preview delay.
          await page.waitForTimeout(800)
          // The preview panel floats over the page; screenshots show the app alone.
          await page.addStyleTag({ content: '.preview-panel { display: none !important; }' })
          await page.evaluate(() => document.fonts.ready)
          if (shots) await page.screenshot({ path: join(folder, `${route.name}.png`), fullPage: true })
          if (scenario !== 'normal') continue
          for (const check of checks.filter((candidate) => candidate.route === route.name)) {
            for (const problem of await check.run(page)) failures.push(`${route.name} (${layout}, ${theme}): ${check.name}: ${problem}`)
          }
        }
        await context.close()
      }
    }
  }
} finally {
  await browser.close()
  await server.close()
}

if (shots) console.log(`Screenshots written to ${output}`)
if (failures.length > 0) {
  console.error(`${failures.length} layout check(s) failed:\n${failures.map((failure) => `  - ${failure}`).join('\n')}`)
  process.exitCode = 1
} else {
  console.log(`All ${checks.length} layout checks passed.`)
}
