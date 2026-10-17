## MODIFIED Requirements

### Requirement: Extract tool
The system SHALL provide `agent.extract(ref, question, answerType, values?)` (safe). `ref` SHALL be a typed reference to an untrusted item (`mail:<message id>`, `rss:<article id>`, `note:<path>`, `web:<result id>` for a search result of the current session, and kinds added by later capabilities); the content SHALL be resolved server-side through the owner-scoped source and SHALL never be returned to the main model. For `web:` references the content SHALL be the result's title and snippet only. An unknown or unresolvable reference SHALL fail with `extract.not_found`. The tool SHALL be hidden when no content source is configured.

#### Scenario: Mail fact without reading
- **WHEN** the model calls `agent_extract` with `mail:4711`, "On which date will the parcel arrive?" and `date`
- **THEN** the result is the date, the mail body is not in the model context, and the run is not tainted

#### Scenario: Another owner's item
- **WHEN** the reference names a mail of another owner's account
- **THEN** the call fails with `extract.not_found`

#### Scenario: Web result of another session
- **WHEN** the reference names a search result id from a different session
- **THEN** the call fails with `extract.not_found`
