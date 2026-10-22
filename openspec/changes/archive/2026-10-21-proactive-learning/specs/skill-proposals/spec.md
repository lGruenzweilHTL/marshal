# Spec Delta

## Purpose

Notices routines the user repeatedly walks Marshal through and drafts them as skills for the user to review and add to their configuration.

## ADDED Requirements

### Requirement: Workflow detection
A nightly job SHALL look, for each owner whose "help Marshal learn" preference is on, at the owner's runs of the last 30 days with outcome `ok`, at least 3 tool calls and no `down` rating. Runs SHALL be grouped when their sequences of tool ids are equal and their input embeddings have similarity of at least 0.8. A group of at least 3 runs that no existing skill covers (a skill whose name or description matches the group, as decided by the drafting model) and that was not proposed or dismissed before SHALL become a skill proposal. The job SHALL read the runs' user inputs, tool ids and argument keys, never tool results or argument values.

#### Scenario: Repeated routine
- **WHEN** in three runs the user asked to "get the printer ready" and each run called `printer_status`, `ha_HassTurnOn` and `printer_preheat` in that order
- **THEN** a skill proposal for that workflow exists after the nightly job

#### Scenario: Results not read
- **WHEN** the job builds a proposal from runs that read mail
- **THEN** no mail content is part of its model input

### Requirement: Skill proposal items
A skill proposal SHALL contain a drafted `SKILL.md` (frontmatter `name` matching `[a-z][a-z0-9-]*` and `description`, and step-by-step instructions naming the tools), the source runs, and, when a skill with the same name exists, a diff against it. The Inbox SHALL list it as an open proposal with preview, diff, download of the file and dismiss. One `low` notification SHALL summarize the new proposals of a night. Marshal SHALL NOT write the file to `/config`.

#### Scenario: Download
- **WHEN** the user opens a skill proposal and downloads it
- **THEN** the downloaded file is the drafted `SKILL.md` and the proposal is marked `downloaded`

#### Scenario: Dismiss
- **WHEN** the user dismisses a proposal
- **THEN** the same workflow is not proposed again
