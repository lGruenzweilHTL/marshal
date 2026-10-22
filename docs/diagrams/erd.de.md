---
title: Datenmodell
icon: fa-solid fa-database
order: 10
description: "Jede EF-Core-Entität von Marshal.Data mit ihren Spalten und Beziehungen, ein Diagramm pro Bereich."
---

# Datenmodell

Jede EF-Core-Entität von `Marshal.Data` mit ihren Spalten und Beziehungen, ein Diagramm pro Bereich. Entitäten aus anderen Bereichen erscheinen als einfache Kästen, wo eine Beziehung Bereiche überquert.

In den Kommentaren der Attribute markiert `null` eine Spalte, die null sein darf; `PK`, `FK` und `UK` (unique) markieren Schlüssel.

> [!NOTE]
> Jede Tabelle, die einem Benutzer gehört, trägt `owner_id`, auch Kindtabellen (denormalisiert), damit jede ihren eigenen auf den Eigentümer beschränkten EF-Core-Abfragefilter bekommt. Ausnahmen: die Tabellen von ASP.NET Identity (`User`, `UserLogin`, `UserPasskey`, `Role`, `UserRole`) und `UserPreferences` mit dem Schlüssel `user_id`. Der Eigentümerfilter von `Secret` gibt auch Zeilen der Instanz zurück (`owner_id` null); nur Admins dürfen diese schreiben. Notizen sind kein Gedächtnis: Sie liegen in `NoteIndexEntry` (nur Suche). Wichtige Inhalte aus Notizen werden zu einem normalen `Chunk`, den der Agent schreibt, mit `source_ref` auf den Pfad der Notiz.

## Identität und Zugriff

Benutzer, Logins, Passkeys, Rollen, API-Tokens, Authentifizierungs-Tickets und Präferenzen.

<!--@include: ../_diagrams/erd-identity.md-->

## Unterhaltungen und Durchläufe

Sitzungen, Durchläufe und ihre Schritte, ausstehende Aktionen und Eingabeanfragen, Apps, Evals, Rückfragen und das Audit-Log.

<!--@include: ../_diagrams/erd-conversations.md-->

## Gedächtnis

Projekte, Einträge mit Versionen und Kanten, Gedächtnisvorschläge sowie die Volltext- und Vektorindizes. Das [Gedächtnismodell](../concepts/memory-model.md) erklärt, wie sie genutzt werden.

<!--@include: ../_diagrams/erd-memory.md-->

## Hintergrundarbeit

Aufgaben, Executor-Sitzungen, Benachrichtigungen, Änderungsvorschläge, Vorschläge, Skill-Vorschläge und Wartungszustand.

<!--@include: ../_diagrams/erd-background-work.md-->

## Integrationen und Secrets

Secrets, Pins und Task-Handles von MCP-Werkzeugen, Mailkonten, Synchronisationszustand von CalDAV, Artefakte, der Notizindex und Zeilen der Demo-Daten.

<!--@include: ../_diagrams/erd-integrations.md-->

## Tabellen und Einschränkungen

Tabellennamen, Indizes und Einschränkungen, die die Diagramme nicht zeigen.

| Entität | Tabelle | Einschränkungen und Hinweise |
|---|---|---|
| `User` | `users` |  |
| `UserLogin` | `user_logins` |  |
| `UserPasskey` | `user_passkeys` |  |
| `Role` | `roles` |  |
| `UserRole` | `user_roles` |  |
| `UserClaim` | `user_claims` |  |
| `UserToken` | `user_tokens` |  |
| `RoleClaim` | `role_claims` |  |
| `Session` |  | unique (api_token_id, external_id), wenn closed_at null ist |
| `PendingInputRequest` | `pending_input_requests` |  |
| `AppInstance` | `app_instances` |  |
| `EvalCase` | `eval_cases` |  |
| `EvalRun` | `eval_runs` |  |
| `EvalResult` | `eval_results` |  |
| `ChunkVersion` |  | vorheriger Inhalt bei einem Update mit derselben ID |
| `MemoryProposal` | `memory_proposals` | unique (owner_id, kind, chunk_a, chunk_b) |
| `ChunkEdge` |  | kein Selbst-Ersetzen (Check) |
| `ChunkFts` | `chunks_fts` | FTS5 |
| `ChunkVersionFts` | `chunk_versions_fts` | FTS5 |
| `ChunkVectors` | `chunk_vectors` | sqlite-vec vec0 |
| `AgentTask` | `agent_tasks` |  |
| `Suggestion` | `suggestions` | index (owner_id, detector, dedupe_key, created_at) |
| `SuggestionDetectorStat` | `suggestion_detector_stats` |  |
| `SkillProposal` | `skill_proposals` | unique (owner_id, sequence_hash) |
| `MaintenanceState` | `maintenance_state` |  |
| `Secret` |  | unique (name), wenn owner_id null ist; unique (owner_id, name), wenn owner_id nicht null ist |
| `McpToolPin` | `mcp_tool_pins` | index (state); Konfiguration der Instanz: keine Spalte für den Eigentümer |
| `McpTaskHandle` | `mcp_task_handles` | unique (server_id, server_task_id); index (next_poll_at) |
| `MailAccount` | `mail_accounts` | unique (owner_id, alias) |
| `CalDavSyncState` | `caldav_sync_state` |  |
| `Artifact` | `artifacts` |  |
| `NoteIndexEntry` | `note_index_entries` | index (owner_id, path); abgeleitet, bei Änderung pro Datei neu gebaut; Stichwortsuche: notes_fts (FTS5), Vektor: note_vectors (sqlite-vec) |
| `DemoSeed` | `demo_seed` | index (owner_id, entity); Demo-Modus: marshal demo reset löscht genau diese Zeilen |
