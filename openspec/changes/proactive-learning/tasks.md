# Tasks

## 1. Suggestion core

- [ ] 1.1 Write failing `BetaAcceptancePolicy` tests: exploration for the first `explore` candidates (inbox); drop below `minAcceptance`; inbox between thresholds; push at or above `pushAcceptance`; expired suggestions do not change counts. Implement `ISuggestionPolicy`. Verify the tests pass.
- [ ] 1.2 Write failing store tests (owner-scoped `suggestions` and `suggestion_detector_stats`, mutes with end time, count reset). Add the tables (migration, `erd.puml`) and implement `ISuggestionStore`. Verify the tests pass.
- [ ] 1.3 Write failing pipeline tests with a fake detector, `FakeChatClient` and a fake notification router: no evaluation when the toggle is off or the detector is muted; dedupe within the cooldown; wording in the owner's language from facts only, without tools; priority and channel from the policy; never voice; quiet hours respected. Implement the pipeline and `suggestions.*` options with validation. Verify the tests pass.
- [ ] 1.4 Write failing tests for proposed actions: frozen with the suggestion's TTL; Accept (inbox or push nonce) runs it once through the guard, a confirm-tier action counts as confirmed; Dismiss runs nothing; both update counts; a wrong nonce is ignored and logged. Implement. Verify the tests pass.

## 2. Detectors

- [ ] 2.1 Verify the HA weather forecast call (`weather.get_forecasts` with response over websocket) against the HA docs and record it in design.md. Write failing `WindowRainDetector` tests with recorded HA fixtures (open window + 80% within the hour → candidate; closed window or low probability → none; daily-only forecast fallback). Implement. Verify the tests pass.
- [ ] 2.2 Write failing `EnergyAnomalyDetector` tests (device at ≥ factor × 14-day median → candidate; less than 14 days of data → none), reusing the energy statistics reader. Implement. Verify the tests pass.
- [ ] 2.3 Write failing `PrintDoneAwayDetector` tests (`printer.complete` while `person` is `not_home` → candidate; with `powerEntity` it proposes switching it off; owner home → none). Add `printers[].powerEntity` (optional, `switch.*` only) with validation, and implement. Verify the tests pass.

## 3. Skill proposals

- [ ] 3.1 Write failing grouping tests: only ok runs with ≥3 tool calls and not rated down in 30 days; equal tool sequences grouped; split by input similarity ≥ 0.8; groups of ≥3 only; no tool results or argument values in the model input. Implement with cached input embeddings. Verify the tests pass.
- [ ] 3.2 Write failing drafting tests with `FakeChatClient`: covered workflows produce nothing; uncovered produce a `SKILL.md` with a valid name and description; dismissed or existing sequence hashes are skipped; a same-name skill produces a diff; one `low` summary notification per night. Add `skill_proposals` (migration, `erd.puml`) and implement the job in the nightly chain. Verify the tests pass.
- [ ] 3.3 Write failing API tests for listing, downloading (marks `downloaded`) and dismissing skill proposals. Implement. Verify the tests pass.

## 4. Maintenance

- [ ] 4.1 Write failing options tests for `maintenance.items` (id pattern, both source kinds, unknown printer, `warnBefore < every`, positive numbers). Implement with validation. Verify the tests pass.
- [ ] 4.2 Verify Moonraker's `/server/history/totals` field names against its docs; write failing counter source tests with recorded fixtures (HA entity state, HA statistic sum, Moonraker hours and meters). Implement `IMaintenanceCounterSource` and both sources. Verify the tests pass.
- [ ] 4.3 Write failing `MaintenanceTracker` tests (`FakeTimeProvider`): baseline on first sight; reset handling; one "soon" and one "due" notification; reads on HA state events, `printer.complete` and hourly. Add `maintenance_state` (migration, `erd.puml`) and implement. Verify the tests pass.
- [ ] 4.4 Write failing tool tests for `maintenance_status` and `maintenance_done` (hidden without items; done resets usage; `persists` escalation in tainted runs). Add `maintenance` to `ReservedNamespaces` and implement. Verify the tests pass.
- [ ] 4.5 Write failing `MaintenanceCounterDetector` tests (printer without item and toggle on → suggestion with YAML snippet; HA `total_increasing` runtime sensors; items already configured → none). Implement. Verify the tests pass.

## 5. Web UI

- [ ] 5.1 Regenerate the web client; write failing component tests for suggestions and skill proposals in the Inbox (order, accept / dismiss, preview, diff, download), the maintenance widget on Home (order, done with confirmation dialog), and detector mute and reset in preferences. Implement with German and English strings; add preview data. Verify `npm test`, `npm run build` and the screenshot sweep pass.

## 6. Documentation and demo

- [ ] 6.1 Document suggestions (detectors, the estimate, muting), skill proposals and maintenance in the guides and the configuration reference (`suggestions.*`, `maintenance.items`, `printers[].powerEntity`); add a commented `maintenance` example to `deploy/config.example/marshal.yaml`. Verify the example config loads in the config load test and the pages follow `docs/development/docs-style.md`.
- [ ] 6.2 Add simulated detectors' data and one maintenance item to `Marshal.Demo` (demo printer hours, a demo window sensor). Verify the demo tests pass.

## 7. Integration check

- [ ] 7.1 Run `dotnet test`, `npm test`, `npm run build` and `openspec validate proactive-learning --strict`. Verify all pass.
