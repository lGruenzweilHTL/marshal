# Spec Delta

## Purpose

Collects Marshal's health warnings in one place so the web UI, the briefing and other tasks can show what needs the user's attention.

## ADDED Requirements

### Requirement: Health warnings
The system SHALL aggregate health warnings from all components (configuration, secrets, database, memory, Home Assistant, MCP servers, pending MCP tool approvals, integrations). Each warning SHALL have a component, a stable code, a severity (`warning` | `error`), a message authored by Marshal in German and English, an optional link into the web UI, and the time it first appeared. Warnings SHALL NOT contain secret values or text received from external systems (error bodies, server messages). `GET /api/v1/system/health` SHALL return the current warnings to human principals and to tokens with `admin`; `/health` SHALL stay unchanged.

#### Scenario: MCP server down
- **WHEN** MCP server `paperless` is unreachable
- **THEN** the warnings contain component `mcp:paperless`, code `mcp.unreachable` and a message that names the server without the transport's error text

#### Scenario: Pending tool approval
- **WHEN** a tool of server `paperless` changed its description and waits for approval
- **THEN** the warnings contain code `mcp.tool_pending` with a link to the configuration view

### Requirement: Health tool
The system SHALL provide `system.health()` (safe) returning the current warnings (component, code, severity, message in the run's language, since). Its output SHALL NOT be untrusted. The tool SHALL be hidden when there are no warnings.

#### Scenario: Briefing mentions warnings
- **WHEN** a briefing task runs while one warning exists
- **THEN** `system_health` is visible and returns that warning

#### Scenario: No warnings
- **WHEN** there are no warnings
- **THEN** `system_health` is not in the tool list

### Requirement: Live health updates
Changes to the warnings SHALL be published on the live-update topic `health`.

#### Scenario: Warning appears
- **WHEN** a new warning appears
- **THEN** connected web clients receive a `health` update without polling
