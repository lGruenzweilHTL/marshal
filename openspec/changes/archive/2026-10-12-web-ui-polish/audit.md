# Visual audit

Sweep: `npm run screenshots` (preview mode, scenarios normal / empty / failing, 375 px and 1280 px, light and dark, English).
Reviewed per route as contact sheets of the four variants. "Phone" is 375 px, "desktop" is 1280 px. Note: the phone tab bar is
fixed, so in full-page screenshots it appears in the middle of long pages. That comes from the capture, not the layout.

## Shared components

| # | Where | Problem | Fix | Status |
|---|---|---|---|---|
| S1 | `Section` (every page with section links) | The "Inbox ›" / "All ›" link ends 4 px inside the content edge because of its own padding | Pull the link back by its padding | done |
| S2 | `FactList` (mail account details, system "About", task details) | Rows have uneven heights when a value is monospace and the next is not | Same line height for both value styles | done |
| S3 | `RunRow`, runs table (home, runs, task detail) | Satellite entity ids (`assist_satellite.living_room`) break mid-word on phones | Show the satellite name without the `assist_satellite.` domain (`living_room`); the run detail keeps the full id | done |
| S4 | `PageHeader` (memory and security tabs) | The header is taller when it has an action button, so the tab bar moves between tabs | Title row has the control height as minimum; map and audit get a subtitle like the other memory tabs | done |
| S5 | Sweep tooling | Some sections without a loading skeleton (health banner, passkeys) were still loading when the shot was taken | Wait for a short settle time after the skeletons are gone | done |

## Views

| # | Route | Variant | Problem | Fix | Status |
|---|---|---|---|---|---|
| V1 | chat | all | One-line composer text 4 px above the send button centre | Textarea uses the control height (layout check in the sweep) | done |
| V2 | home, inbox | desktop | Approval tickets side by side: action rows at different heights; the countdown wraps below a long tool chip | Ticket is a column with the actions at the bottom; the chip shrinks with an ellipsis | done |
| V3 | printers | all | External-link icon of "Open the printer's own interface" wraps onto its own line | Keep the link on one line | done |
| V4 | printers | all | Temperature chart shows only guide lines until a second live sample arrives | One sample draws a flat line across the chart | done |
| V5 | printers, task | all | "Cancel print" / "Cancel task" use a square icon that reads as a checkbox | Use the circle-stop icon | done |
| V6 | approval | all | The "Run" row in "Where it came from" always shows a microphone | Use the runs icon | done |
| V7 | proposal | all | "0 files" for a diff without `diff ` header lines | Count files by their `+++` header | done |
| V8 | artifacts | phone | The "Third-party content" badge squeezes names to one letter per line | On phones the badge moves below the subtitle | done |
| V9 | cost | all | Tier names lowercase ("fast", "standard") next to "Advisor" | Translated tier labels | done |
| V10 | memory | phone | The pin marker of a pinned chunk pushes its title out of line with the other rows | Pin marker after the title | done |
| V11 | memory-projects | phone | Candidate alias chips wrap inside the chip | Chip content on one line; chips wrap as a whole | done |
| V12 | memory-map | desktop | Node labels at the left and right edge are cut off | Labels near an edge are anchored inwards | done |
| V13 | preferences | phone | Quiet-hours time fields cut off "10:30 PM" | Narrower inner padding for time fields in that grid | done |
| V14 | config | desktop | Long MCP target URLs are cut off at the card edge | Allow the URL to wrap | done |
| V15 | tasks | phone | Search field narrower than the cards below it | Full width on phones | done |
| V16 | home | empty scenario | Cost card says "Last 0 days" | Show the range of the request (14 days) | done |

Routes without findings: notification, secrets, security, system (apart from S2), runs (apart from S3), run, not-found.

## Follow-ups (not alignment, left for a later change)

- Memory chunk links and the notification source show raw ids instead of the linked chunk's title or the task name.
- Costs use 2–4 decimals by design, so columns of the cost table are ragged.

## German pass

Full sweep in German (`--scenario normal --lang de`) reviewed at 375 px and 1280 px, light and dark. No overflow or clipped labels. The only issue was the phone time fields in Preferences (two fields per row); they now use less inner padding and no picker icon, and the time lines up with the day select above it.
