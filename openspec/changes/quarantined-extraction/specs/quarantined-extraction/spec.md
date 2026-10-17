# Spec Delta

## Purpose

Answers typed questions about untrusted content with a tool-less model, so the main model can use facts from mail, news, notes or web results without reading the text and, for typed answers, without tainting the run.

## ADDED Requirements

### Requirement: Quarantined extractor
The system SHALL provide an internal extractor that receives untrusted content, a question and an answer type, calls a model of the `fast` tier with no tools, with the content wrapped as untrusted data and the instruction to answer only the question in the given type, and validates the answer. Answer types SHALL be `boolean`, `number`, `integer`, `date`, `datetime`, `enum` (with caller-given values) and `text` (at most 500 characters). An answer that fails validation after one repair attempt SHALL fail with `extract.invalid_answer`. The extractor SHALL also allow `unknown` as an answer of every type. Extractor calls SHALL be recorded as model-call steps of the calling run.

#### Scenario: Typed answer
- **WHEN** the extractor is asked "Is this a parcel delivery notice?" with type `boolean` about a mail body
- **THEN** it returns `true`, `false` or `unknown`

#### Scenario: Enum outside the values
- **WHEN** the model answers `maybe` for an enum with values `yes`, `no` twice
- **THEN** the extraction fails with `extract.invalid_answer`

### Requirement: Taint by answer type
An extraction answer of type `boolean`, `number`, `integer`, `date`, `datetime`, `enum` or `unknown` SHALL NOT taint the calling run. An answer of type `text` SHALL taint the calling run with the content's taint source and SHALL be wrapped as untrusted data.

#### Scenario: Date does not taint
- **WHEN** an untainted run extracts the delivery date of a mail as `date`
- **THEN** the run stays untainted and a following `tasks_create` executes without confirmation

#### Scenario: Text taints
- **WHEN** an untainted run extracts a summary of a mail as `text`
- **THEN** the run is tainted with source `mail`

### Requirement: Extract tool
The system SHALL provide `agent.extract(ref, question, answerType, values?)` (safe). `ref` SHALL be a typed reference to an untrusted item (`mail:<message id>`, `rss:<article id>`, `note:<path>`, and kinds added by later capabilities); the content SHALL be resolved server-side through the owner-scoped source and SHALL never be returned to the main model. An unknown or unresolvable reference SHALL fail with `extract.not_found`. The tool SHALL be hidden when no content source is configured.

#### Scenario: Mail fact without reading
- **WHEN** the model calls `agent_extract` with `mail:4711`, "On which date will the parcel arrive?" and `date`
- **THEN** the result is the date, the mail body is not in the model context, and the run is not tainted

#### Scenario: Another owner's item
- **WHEN** the reference names a mail of another owner's account
- **THEN** the call fails with `extract.not_found`
