## 1. Setup and verification

- [x] 1.1 Verify against current HA docs / source: conversation entity chat-log streaming API, `ConversationInput` fields (device, satellite, extra system prompt, user), STT vs typed signal; record findings in `design.md`
- [x] 1.2 Scaffold `ha/custom_components/marshal` (`manifest.json`, `const.py`, `strings.json`, translations `en` / `de`), `ha/hacs.json`, `ha/tests/` with `pytest-homeassistant-custom-component`

## 2. Client

- [x] 2.1 Write tests for `MarshalClient`: `/api/versions` parsing, SSE parsing (`delta`, `status`, final), auth errors, timeouts
- [x] 2.2 Implement `MarshalClient` with aiohttp and a minimal SSE parser

## 3. Config flow

- [x] 3.1 Write config flow tests: success, unsupported version, unreachable, invalid token
- [x] 3.2 Implement the config flow and translated errors

## 4. Conversation entity

- [x] 4.1 Write tests for `TurnMapper`: field mapping, area lookup from device registry, generated conversation id, `inputMode` detection, `followupRef` extraction (marker present / absent / malformed)
- [x] 4.2 Implement `TurnMapper`
- [x] 4.3 Write entity tests: turn forwarded with mocked Marshal, deltas streamed in order, final speech and `continue_conversation`, `status` not spoken, errors spoken in the turn language
- [x] 4.4 Implement the conversation entity with streaming

## 5. Documentation

- [x] 5.1 Write `ha/README.md`: installation via HACS custom repository, token creation (`marshal tokens create --scope conversation:write`), pipeline setup with local command handling off
- [x] 5.2 Update `packages.puml` if the component's dependencies differ from the plan; re-render
