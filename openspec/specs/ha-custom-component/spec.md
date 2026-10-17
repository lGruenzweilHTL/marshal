# ha-custom-component Specification

## Purpose
The Home Assistant custom integration that makes Marshal a conversation agent for Assist: setup, field mapping, streaming and follow-ups.
## Requirements
### Requirement: Config flow with version check
The integration SHALL be set up through a config flow asking for the Marshal base URL and an API token. The flow SHALL call `GET /api/versions` and SHALL fail with a clear, translated error when the API version the component targets (v1) is not supported, when Marshal is unreachable, or when the token is rejected.

#### Scenario: Supported version
- **WHEN** `/api/versions` lists `1.0` and the token is valid
- **THEN** the config entry is created

#### Scenario: Unsupported version
- **WHEN** `/api/versions` does not list `1.0`
- **THEN** the flow shows the error "Marshal does not support this integration's API version" and no entry is created

#### Scenario: Invalid token
- **WHEN** a test call with the token returns 401 or 403
- **THEN** the flow shows an authentication error

### Requirement: Conversation agent
The integration SHALL register a conversation entity that supports German and English and forwards every turn to `POST /api/v1/conversation/turns`. It SHALL NOT send entity states or other HA data beyond the turn fields.

#### Scenario: Turn forwarded
- **WHEN** Assist delivers "wie weit ist der Druck?" in German from a satellite
- **THEN** Marshal receives one request with `text`, `language = de`, `conversationId`, `deviceId`, `satelliteId`, `areaId`, `haUserId` and `inputMode`

### Requirement: Field mapping
The component SHALL map HA's snake_case fields to Marshal's camelCase JSON: `conversation_id` → `conversationId`, `device_id` → `deviceId`, `satellite_id` → `satelliteId`, the device's area → `areaId`, the context user → `haUserId`. It SHALL generate a conversation id when HA provides none and return it to HA.

#### Scenario: Area from device registry
- **WHEN** the turn's device is assigned to area `kitchen`
- **THEN** the request carries `areaId = kitchen`

#### Scenario: No conversation id
- **WHEN** HA provides no conversation id
- **THEN** the component generates one, sends it, and returns it as the result's conversation id

### Requirement: Input mode
The component SHALL set `inputMode = voice` when the turn came through speech-to-text (satellite or app microphone) and `inputMode = text` when it was typed.

#### Scenario: Typed in the Assist dialog
- **WHEN** a user types a message in the HA app's Assist dialog
- **THEN** the request carries `inputMode = text`

#### Scenario: Spoken on a satellite
- **WHEN** a turn comes from an Assist satellite pipeline run with STT
- **THEN** the request carries `inputMode = voice`

### Requirement: Streaming into HA
The component SHALL read Marshal's SSE stream and pass `delta` events to HA's chat log as streamed assistant content, so HA can start TTS before the reply is complete. `status` events SHALL NOT be spoken. The final event SHALL set the response speech and `continue_conversation`.

#### Scenario: Deltas streamed
- **WHEN** Marshal sends three `delta` events and a final event
- **THEN** HA receives the three chunks in order and the final speech equals their concatenation or the final `speech`

#### Scenario: Continue conversation
- **WHEN** the final event has `continueConversation = true`
- **THEN** the conversation result has `continue_conversation = True`

### Requirement: Follow-up reference
When HA passes an `extra_system_prompt` containing `marshal-followup:<nonce>`, the component SHALL send `followupRef = <nonce>` with the turn and SHALL NOT forward the rest of the extra system prompt.

#### Scenario: Follow-up turn
- **WHEN** the reply to a `start_conversation` announcement arrives with `extra_system_prompt = "marshal-followup:abc123"`
- **THEN** the request carries `followupRef = abc123`

#### Scenario: Other extra prompt
- **WHEN** the extra system prompt contains no `marshal-followup:` marker
- **THEN** `followupRef` is null

### Requirement: Errors are spoken briefly
When Marshal is unreachable, times out or returns an error, the component SHALL return a short spoken error in the turn's language and SHALL NOT raise.

#### Scenario: Marshal down
- **WHEN** the connection to Marshal fails
- **THEN** the response speech says Marshal is unreachable, in the turn's language

### Requirement: AI Task entity
The integration SHALL register an `ai_task` entity that supports generating data and attachments, and no image generation. For each task it SHALL send the task name, the instructions, the structure converted to a JSON Schema, HA's configured language and the resolved attachment files to `POST /api/v1/ai-tasks`, and SHALL return Marshal's `data` with the chat log's conversation id. It SHALL NOT send entity states or other HA data beyond the task fields. Errors from Marshal (unreachable, 403, 4xx, 5xx, timeout) SHALL raise a translated Home Assistant error that names the cause; a 403 SHALL say that the token lacks `aitasks:write`.

#### Scenario: Task forwarded
- **WHEN** an automation calls `ai_task.generate_data` on Marshal's entity with instructions, a structure and a camera snapshot
- **THEN** Marshal receives one multipart request with the task JSON, the JSON Schema and the image, and the action's response contains Marshal's data

#### Scenario: Token without scope
- **WHEN** Marshal answers 403
- **THEN** the action fails with an error saying the token needs the `aitasks:write` scope

#### Scenario: Reauth for a new token
- **WHEN** the AI task endpoint answers 403
- **THEN** HA shows a reauthentication flow for the Marshal entry, and entering a valid token with `aitasks:write` updates the entry without removing it

#### Scenario: Structure conversion
- **WHEN** the structure has a required boolean field and an optional text field
- **THEN** the JSON Schema has a boolean property and a string property, and only the first is listed as required
