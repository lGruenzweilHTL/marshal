## ADDED Requirements

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
