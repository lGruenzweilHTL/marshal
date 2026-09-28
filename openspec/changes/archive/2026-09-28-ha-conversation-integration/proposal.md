## Why

Voice through Home Assistant Assist is Marshal's main interface, but HA can only use Marshal once a conversation agent forwards each turn to it. `core-agent` already exposes the public conversation API, so the HA side can be built and tested against that contract alone. This change (sub-project 3 of 7) adds the thin Python custom integration.

## What Changes

- New HACS-installable custom integration `custom_components/marshal` in `ha/`.
- Config flow: Marshal URL and API token (scope `conversation:write`); setup checks `GET /api/versions` and shows a clear error when the component's API version is unsupported.
- A conversation entity that forwards each turn to `POST /api/v1/conversation/turns`: text, language, conversation id, device, satellite, area, HA user, `inputMode`, and `followupRef` extracted from `extra_system_prompt`.
- Streams Marshal's `delta` events into HA's chat log so HA can stream into TTS; maps the final event to HA's response and `continue_conversation`.
- snake_case (HA) ↔ camelCase (Marshal) mapping at the component's edge.
- Tests with `pytest-homeassistant-custom-component`, Marshal mocked.
- Setup notes: the Assist pipeline must not use "prefer handling commands locally".

## Non-goals

- No entity dumps or state sent to Marshal: Marshal reads HA state through HA MCP / REST itself (`extensibility`).
- No HA sidebar panel.
- No voice testing on the Marshal side (Marshal only receives text).
- No decision yet on moving `ha/` into its own repository (LEANING, decided when HACS publishing starts).
- No use of `followupRef` by Marshal yet; the component forwards it, `background-work` consumes it.

## Capabilities

### New Capabilities
- `ha-custom-component`: config flow, version check, conversation entity, turn forwarding, streaming, response mapping, `inputMode` detection, follow-up reference extraction, error handling.

### Modified Capabilities
- None.

## Impact

- New code: `ha/custom_components/marshal/` (`manifest.json`, `config_flow.py`, `conversation.py`, `api.py`, `const.py`, translations `en` / `de`), `ha/hacs.json`, `ha/tests/`.
- Consumes: `/api/versions`, `/api/v1/conversation/turns` (from `core-agent`).
- New dev dependency: `pytest-homeassistant-custom-component`.
