---
title: Data model
icon: fa-solid fa-database
order: 10
description: "Every EF Core entity of Marshal.Data with its columns and relationships, one diagram per area."
---

# Data model

Every EF Core entity of `Marshal.Data` with its columns and relationships, one diagram per area. Entities from other areas appear as plain boxes where a relationship crosses areas.

In the attribute comments, `null` marks a nullable column; `PK`, `FK` and `UK` (unique) mark keys.

> [!NOTE]
> Every user-owned table carries `owner_id`, child tables included (denormalized), so each one gets its own owner-scoped EF Core query filter. Exceptions: the ASP.NET Identity tables (`User`, `UserLogin`, `UserPasskey`, `Role`, `UserRole`) and `UserPreferences`, keyed by `user_id`. The owner filter of `Secret` also returns instance rows (`owner_id` null); only admins may write those. Notes are not memory: they live in `NoteIndexEntry` (search only). Important note content becomes a normal `Chunk` written by the agent, with `source_ref` set to the note path.

## Identity and access

Users, logins, passkeys, roles, API tokens, authentication tickets and preferences.

<!--@include: ../_diagrams/erd-identity.md-->

## Conversations and runs

Sessions, runs and their steps, pending actions and input requests, apps, evals, follow-ups and the audit log.

<!--@include: ../_diagrams/erd-conversations.md-->

## Memory

Projects, chunks with versions and edges, memory proposals, and the full-text and vector indexes. The [memory model](../concepts/memory-model.md) explains how they are used.

<!--@include: ../_diagrams/erd-memory.md-->

## Background work

Tasks, executor sessions, notifications, proposals, suggestions, skill proposals and maintenance state.

<!--@include: ../_diagrams/erd-background-work.md-->

## Integrations and secrets

Secrets, MCP tool pins and task handles, mail accounts, CalDAV sync state, artifacts, the notes index and demo seed rows.

<!--@include: ../_diagrams/erd-integrations.md-->

## Tables and constraints

Table names, indexes and constraints that the diagrams do not show.

| Entity | Table | Constraints and notes |
|---|---|---|
| `User` | `users` |  |
| `UserLogin` | `user_logins` |  |
| `UserPasskey` | `user_passkeys` |  |
| `Role` | `roles` |  |
| `UserRole` | `user_roles` |  |
| `UserClaim` | `user_claims` |  |
| `UserToken` | `user_tokens` |  |
| `RoleClaim` | `role_claims` |  |
| `Session` |  | unique (api_token_id, external_id) where closed_at is null |
| `PendingInputRequest` | `pending_input_requests` |  |
| `AppInstance` | `app_instances` |  |
| `EvalCase` | `eval_cases` |  |
| `EvalRun` | `eval_runs` |  |
| `EvalResult` | `eval_results` |  |
| `ChunkVersion` |  | previous content of a same-id update |
| `MemoryProposal` | `memory_proposals` | unique (owner_id, kind, chunk_a, chunk_b) |
| `ChunkEdge` |  | no self-supersede (check) |
| `ChunkFts` | `chunks_fts` | FTS5 |
| `ChunkVersionFts` | `chunk_versions_fts` | FTS5 |
| `ChunkVectors` | `chunk_vectors` | sqlite-vec vec0 |
| `AgentTask` | `agent_tasks` |  |
| `Suggestion` | `suggestions` | index (owner_id, detector, dedupe_key, created_at) |
| `SuggestionDetectorStat` | `suggestion_detector_stats` |  |
| `SkillProposal` | `skill_proposals` | unique (owner_id, sequence_hash) |
| `MaintenanceState` | `maintenance_state` |  |
| `Secret` |  | unique (name) where owner_id is null; unique (owner_id, name) where owner_id is not null |
| `McpToolPin` | `mcp_tool_pins` | index (state); instance configuration: no owner column |
| `McpTaskHandle` | `mcp_task_handles` | unique (server_id, server_task_id); index (next_poll_at) |
| `MailAccount` | `mail_accounts` | unique (owner_id, alias) |
| `CalDavSyncState` | `caldav_sync_state` |  |
| `Artifact` | `artifacts` |  |
| `NoteIndexEntry` | `note_index_entries` | index (owner_id, path); derived, rebuilt per file on change; keyword: notes_fts (FTS5), vector: note_vectors (sqlite-vec) |
| `DemoSeed` | `demo_seed` | index (owner_id, entity); demo mode: marshal demo reset deletes exactly these rows |
