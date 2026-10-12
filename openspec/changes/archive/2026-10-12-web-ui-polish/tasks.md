# Tasks

## 1. Composer alignment

- [x] 1.1 Write failing layout checks in the sweep script (jsdom cannot measure layout): the one-line composer text is centred on the send button (within 1 px), and with a multi-line draft the button stays at the bottom edge; verify the first check fails with the 4 px offset from the screenshot
- [x] 1.2 Change the composer textarea to the control height (22 px line, 11 px vertical padding) and verify both checks pass at 375 px and 1280 px, light and dark

## 2. Screenshot sweep

- [x] 2.1 Add Playwright as a web dev dependency (no browser download, `PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD` respected), git-ignore `web/.screenshots/`, and add it to the CLAUDE.md package index; verify `npm ci` works without downloading a browser
- [x] 2.2 Write `web/scripts/screenshots.ts` and `npm run screenshots` (preview server, every route, 375 / 1280 px, light / dark, scenarios normal / empty / failing, optional `--lang de`); verify it writes one PNG per route and variant
- [x] 2.3 Document the sweep in `docs/development/` (how to run, where the output goes, the container fallback with `sg docker`) and verify the documented command runs as written

## 3. Audit and fixes

- [x] 3.1 Run the sweep, review all screenshots, and record every finding in `openspec/changes/web-ui-polish/audit.md` (view, variant, problem, planned fix); verify every route appears in the audit, with findings or with "no findings"
- [x] 3.2 Fix findings caused by shared components in `web/src/ui`, with a component test for each changed rule where it can be tested in jsdom; verify `npm test` and `npm run build` pass and a new sweep shows them fixed
- [x] 3.3 Fix view-specific findings; verify a new sweep shows them fixed and mark each item done in `audit.md`
- [x] 3.4 Re-run the full sweep in German and confirm no overflow at 375 px; record the result in `audit.md`
