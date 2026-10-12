# Proposal

## Why

The web UI has visible alignment mistakes. The most visible one is the chat composer: the placeholder text sits higher than the centre of the send button, because the one-line textarea (36 px) and the 44 px button are aligned to the bottom edge. Nobody has done a systematic visual pass over all views in both layouts and both themes, so more mistakes like this are likely. This change goes first, before the chat changes (`chat-history-and-background`, `chat-reasoning`), so their new UI builds on a corrected base.

## What Changes

- Fix the chat composer: the text line and the send button share one vertical centre while the draft is one line, and the button stays at the bottom edge when the draft grows to several lines.
- Add a repeatable screenshot sweep: a Playwright script that opens every route of preview mode at phone (375 px) and desktop (1280 px) width, in light and dark, and writes the screenshots to a local folder for review. It is a developer tool and does not run in CI.
- Review the screenshots, record every misalignment in `audit.md` in this change, and fix them in this change (spacing, vertical centring, baselines, overflow at 375 px, inconsistent control heights).
- Add a component-level rule to the design system: controls placed side by side in one row share a vertical centre and use the shared control heights.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `web-shell`: the design system requirement gains the row-alignment and control-height rule.

## Non-goals

- New visual design, new tokens or a theme change. Only fix deviations from the existing design.
- Automated visual regression in CI (pixel diffs). The sweep is a manual review tool.
- Changes to view behavior or API contracts.

## Impact

- `web/src/chat/ChatView.vue` (composer), plus every view and `web/src/ui` component the audit names.
- New `web/scripts/screenshots.ts` and an `npm run screenshots` script; Playwright as a dev dependency (it uses the pre-installed Chromium or the screenshot container, and never downloads browsers).
- CLAUDE.md package index (web: Playwright, dev only).
