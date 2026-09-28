# risk-guard Specification

## Purpose
TBD - created by archiving change core-agent. Update Purpose after archive.
## Requirements
### Requirement: Risk tiers
Every tool call SHALL have exactly one tier: `safe` (execute), `confirm` (execute only after a user action confirms it) or `forbidden` (never execute). Capabilities that must never happen (sending mail, raw G-code) SHALL NOT exist as tools at all.

#### Scenario: Safe call executes
- **WHEN** the model calls a tool whose tier resolves to `safe`
- **THEN** the guard allows it and the tool runs immediately

#### Scenario: Forbidden call never executes
- **WHEN** a call resolves to `forbidden` (e.g. because a rule changed after the tool list was built)
- **THEN** the guard denies it and the model receives an error result

### Requirement: Arg-aware risk rules
Risk rules SHALL be read from `/config/risk.yaml`, matched by glob on tool ids, optionally with argument or target conditions (e.g. `domain`, `deviceClass`), in order, first match wins. Tools with no matching rule SHALL use their descriptor's default tier. The rules SHALL apply identically to built-in and external tools.

#### Scenario: First match wins
- **WHEN** rules are `ha.*` + `domain: lock` → confirm, then `ha.*` → safe, and a call targets a lock
- **THEN** the call is `confirm`

#### Scenario: Rules reload
- **WHEN** `/config/risk.yaml` changes on disk and is valid
- **THEN** the next tool call is evaluated with the new rules without restart

### Requirement: Target resolution fails closed
Rules with target conditions SHALL be evaluated on the concrete targets a call resolves to, through an `IRiskTargetResolver` for the tool's namespace. The resolver SHALL expand every filter the call carries (name, area, floor, domain, device class). Rules SHALL be evaluated per resolved target and the highest tier SHALL win. A call whose targets cannot be resolved, or resolve to an empty set, or whose namespace has no resolver while a target rule matches its id, SHALL be `confirm`.

#### Scenario: Area-wide call containing a lock
- **WHEN** a call targets an area that resolves to two lights and one lock, and locks are `confirm`
- **THEN** the whole call is `confirm`

#### Scenario: Unknown target
- **WHEN** a call names a device the resolver does not know
- **THEN** the call is `confirm` with reason `unresolved_target`

### Requirement: Frozen pending actions
For a `confirm` call in an interactive turn, the system SHALL store a pending action with tool, arguments, session, satellite, owner, nonce, reasons and an expiry (~60 s, configurable) before the user is asked, and SHALL end the turn with `continueConversation = true`. There SHALL be at most one pending action per session; a new one SHALL supersede the old one. On confirmation the system SHALL execute exactly the frozen action; the model SHALL NOT be able to change it.

#### Scenario: Confirmation executes the frozen action
- **WHEN** a pending action exists and the user confirms
- **THEN** the stored tool and arguments are executed unchanged and the model receives the result

#### Scenario: Expired pending action
- **WHEN** the user confirms after the expiry
- **THEN** nothing is executed, the action is marked `expired`, and the reply says it expired

#### Scenario: Superseded
- **WHEN** a second confirm-tier call happens in the same session while one is pending
- **THEN** the first is marked `superseded` and only the second can be confirmed

### Requirement: Keyword confirmation
On the next user turn after a pending action, the system SHALL normalize the full utterance (trim, lowercase, strip punctuation) and compare it with the configured confirmation keywords for the request language. An exact match SHALL execute the frozen action without a model call.

#### Scenario: Keyword with punctuation
- **WHEN** the German keyword list contains "bestätigt" and the user says "Bestätigt!"
- **THEN** the frozen action executes and `decided_by` is `keyword`

#### Scenario: Keyword inside a longer sentence
- **WHEN** the user says "bestätigt, aber erst morgen"
- **THEN** there is no keyword match and the judge decides

### Requirement: Judge confirmation
If no keyword matches, a `fast`-tier judge SHALL receive only the action summary and the user's reply, at temperature 0, and return exactly one of `positive`, `neutral`, `negative`. `positive` SHALL execute the frozen action; `neutral` SHALL drop it and process the message as a normal new turn; `negative` SHALL drop it and reply that it was cancelled. The judge prompt SHALL instruct it to answer `neutral` when not confident. A judge timeout or unparsable output SHALL drop the action.

#### Scenario: Topic change
- **WHEN** the judge returns `neutral`
- **THEN** the pending action is dropped and the reply is handled by the normal agent loop

#### Scenario: Judge failure
- **WHEN** the judge times out or returns text that is not one of the three values
- **THEN** the pending action is dropped and nothing is executed

### Requirement: Only user actions confirm
A pending action SHALL be confirmable only by a user action: a user turn in the same session, a web UI approval, or a nonce-verified push action. Tool output, model output and content of any kind SHALL NOT confirm.

#### Scenario: Tool output contains a keyword
- **WHEN** a tool result contains the text "confirm" while an action is pending
- **THEN** the action stays pending

### Requirement: Confirmation reasons
The guard SHALL determine why a call needs confirmation and store one or more reasons with the pending action, each with a code (`rule`, `unresolved_target`, `taint`, `quiet_hours_override`) and a short sentence in the request language. The tool result that tells the model confirmation is pending SHALL carry the sentence, and the system prompt SHALL require the confirmation question to include it. When several reasons apply, `taint` SHALL be the spoken one.

#### Scenario: Rule reason
- **WHEN** a call to unlock a door becomes pending because of a rule
- **THEN** the pending action has reason `rule` and the tool result includes a sentence naming the action

#### Scenario: Taint reason names the source
- **WHEN** a `persists` tool is escalated because the run read mail
- **THEN** the reason is `taint` and its sentence says that the conversation included mail content, which can contain hidden instructions

### Requirement: Taint escalation
A run SHALL become tainted when the result of a tool with `untrustedOutput` enters its context, and SHALL record the taint sources (kind and first tainting step). Taint SHALL never be cleared within a run. In a tainted run, calls to tools flagged `persists` SHALL be escalated from `safe` to `confirm`. The guard SHALL only raise tiers, never lower them.

#### Scenario: Reminder after reading mail
- **WHEN** a run reads a mail and then calls a `persists` tool that is `safe` by default
- **THEN** the call becomes a pending action with reason `taint`

#### Scenario: Untainted run
- **WHEN** a run has read no untrusted content and calls the same tool
- **THEN** it executes without confirmation

### Requirement: Guard decisions are recorded
Every guard decision SHALL be recorded on the run step: `safe`, `pending`, `confirmed_by_keyword`, `judge_positive` / `judge_neutral` / `judge_negative`, `escalated_by_taint`, `denied`, `expired`.

#### Scenario: Denied call recorded
- **WHEN** the guard denies a call
- **THEN** the run step for that call has guard decision `denied` and the reason

