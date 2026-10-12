# Design

## Context

`.composer` in `web/src/chat/ChatView.vue` is a flex row with `align-items: flex-end`. The textarea is 36 px tall (`min-height: 36px`, 22 px line plus 7 px padding at top and bottom). The send button is an `AppButton` of `--target` height (44 px). Bottom alignment puts the text line centre 4 px above the button centre. The screenshot from the user shows this.

Preview mode (`npm run dev:preview`) already runs every view against the fake API with scenarios (normal, empty, failing, unreachable). That makes it the target for a screenshot sweep without the .NET host.

## Goals / Non-Goals

**Goals:**
- Correct composer alignment for one-line and multi-line drafts.
- A repeatable way to see every view in four variants (phone / desktop × light / dark).
- A recorded audit and fixes for everything it finds.

**Non-Goals:**
- Pixel-diff regression tests.
- Redesigning components.

## Decisions

**Composer: match heights, keep bottom alignment.** Give the textarea the control height (44 px minimum: 22 px line plus 11 px padding at top and bottom), and keep `align-items: flex-end`. For one line, both elements are 44 px, so their centres match. For several lines, the button stays at the bottom edge, as the spec scenario requires. Alternative: `align-items: center` plus a JS class switch when the draft wraps. Rejected: it needs line counting, and the button jumps when the draft wraps.

**Sweep script with Playwright, in `web/scripts/screenshots.ts`.** It starts the preview server (Vite in `preview` mode), reads the route list from `web/src/router.ts` (static list of named routes plus sample ids from the preview fixtures), and writes `web/.screenshots/<scenario>/<width>-<theme>/<route>.png` (git-ignored). Theme comes from the per-device appearance setting in `localStorage`. The language uses the preview panel setting, English by default, with an optional `--lang de` because German labels are longer and show overflow. Playwright launches with `executablePath` from `PLAYWRIGHT_CHROMIUM` or `/opt/pw-browsers/chromium` when set; the script never downloads a browser. Alternative: the existing Playwright screenshot container. Kept as a fallback through the same script (`sg docker`), not as the main path.

**Audit as a change artifact.** `audit.md` lists each finding: view, variant, what is wrong, fix, status. Fixes go into shared components (`web/src/ui`) where the cause is shared, not into per-view overrides.

**No new interfaces and no model-facing tools.** The change is web-only.

## Risks / Trade-offs

- [The audit grows large] → Fix shared-component causes first, then view-specific ones. Move items that need design decisions (not alignment) to a follow-up list in `audit.md` instead of widening the change.
- [The 44 px textarea makes the composer taller] → The button already sets the composer height (44 px plus padding), so the outer size does not change.
- [Preview fixtures do not cover every state] → Use the empty and failing scenarios as well. A state that no scenario can reach is noted in the audit, and the fixture is extended when that is cheap.
