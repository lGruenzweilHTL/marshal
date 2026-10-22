---
title: Maintaining the diagrams
icon: fa-solid fa-pen-ruler
description: "Where the Mermaid diagrams live, when to update them and how to preview them."
---

# Maintaining the diagrams

The diagrams are [Mermaid](https://mermaid.js.org) code blocks in snippet files in `docs/_diagrams/`. The pages include them with `<!--@include: ../_diagrams/<name>.md-->`, so every diagram has one file to edit, whichever pages and languages show it. Clay draws them on the site. GitHub draws them when it shows the snippet file; the page itself shows nothing there, because the include is an HTML comment.

| Diagram | Snippet | Language |
|---|---|---|
| Packages | `packages.md` | One file for all languages: it holds project and package names |
| Data model | `erd-<area>.md` | One file for all languages: it holds entity and column names |
| Voice turn | `voice-turn.md`, `voice-turn.de.md` | One file per language |
| Confirm-tier action | `confirm-tier-action.md`, `confirm-tier-action.de.md` | One file per language |
| Task lifecycle | `task-lifecycle.md`, `task-lifecycle.de.md` | One file per language |

Diagrams of code (names of projects, packages, entities, columns) are never translated: the names are the code. Flow diagrams have labels in prose, so the German pages include a German copy. Change both files of a flow in the same commit; they keep the same participants, states and arrows in the same order, so a side-by-side diff shows what differs.

| Diagram | Page |
|---|---|
| Packages | [Architecture](../concepts/architecture.md#the-parts) |
| Voice turn | [Architecture](../concepts/architecture.md#how-a-request-moves-through-it) |
| Confirm-tier action | [Risk and confirmation](../concepts/risk-and-confirmation.md#frozen-pending-actions) |
| Task lifecycle | [Tasks and scheduling](../concepts/tasks-and-scheduling.md#the-lifecycle-of-a-run) |
| Data model, one diagram per area | [Data model](erd.md) |

## Keep them up to date

The diagrams must always match the code. Each snippet starts with a `%% KEEP UP TO DATE` comment that says what it describes.

- **Data model:** update the `erd-<area>.md` snippets of [the data model](erd.md) in the same change that adds, removes or renames an EF Core entity, column or relationship. Put the entity in the diagram of its area, and add table names, indexes and constraints to the table at the end of `erd.md` and its translation.
- **Packages:** update `packages.md` (shown in [architecture](../concepts/architecture.md)) in the same change that adds, removes or renames a project in `src/`, `web/` or `ha/`, or changes a project reference or key external dependency.
- **Flows:** update the voice turn, confirm-tier and task lifecycle snippets, English and German, in the same change that alters the behavior they show.

## Preview

- `npx clay-doc@5 dev` draws the diagrams with live reload (Node.js 22 or newer).
- `npx clay-doc@5 check` does not draw diagrams, so it does not catch Mermaid syntax errors. Look at the page in the dev server, or paste the block into the [Mermaid live editor](https://mermaid.live).
- Editors: the Markdown preview of VS Code (with a Mermaid extension) and JetBrains IDEs draws `mermaid` blocks.
