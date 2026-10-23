---
title: Maintaining the diagrams
icon: fa-solid fa-pen-ruler
description: "Where the Mermaid diagrams live, how small they stay, when to update them and how to preview them."
---

# Maintaining the diagrams

The diagrams are [Mermaid](https://mermaid.js.org) snippets in `docs/_diagrams/`, included with `<!--@include: ../_diagrams/<name>.md-->`, so each diagram has one file to edit. On GitHub the including page shows nothing, because the include is an HTML comment.

| Diagram | Snippets | Language | Page |
|---|---|---|---|
| Project references | `packages.md` | One file | [Architecture](../concepts/architecture.md#the-parts) |
| Runtime connections | `packages-runtime.md` | One file | [Architecture](../concepts/architecture.md#the-parts) |
| Voice turn | `voice-turn-request`, `voice-turn-agent-loop` | One file per language | [Architecture](../concepts/architecture.md#how-a-request-moves-through-it) |
| Confirm-tier action | `confirm-tier-freeze`, `confirm-tier-confirm` | One file per language | [Risk and confirmation](../concepts/risk-and-confirmation.md#frozen-pending-actions) |
| Task lifecycle | `task-lifecycle`, `task-run` | One file per language | [Tasks and scheduling](../concepts/tasks-and-scheduling.md#the-lifecycle-of-a-run) |
| Data model | `erd-<area>-<part>.md` | One file | [Data model](erd.md) |

Code diagrams (project, entity and column names) are never translated. Flow diagrams have a German copy (`<name>.de.md`) with the same participants, states and arrows in the same order; change both in the same commit.

## Size

The content column of the site is about 640 px wide. Clay draws most diagrams at build time (`beautiful-mermaid`) and scales a wider one down until its text is hard to read. Keep each diagram at most about 800 px wide:

- Start the block with the diagram type (`sequenceDiagram`, `erDiagram`, ...), then the `%%` comments. A block that does not start with its type falls back to drawing in the browser, without the site's theme.
- Split a flow into parts of at most five participants or about eight states, each with its own snippet.
- Sequence diagrams: one short line per message (about 30 characters), no `<br/>` in messages and no notes between messages; the build-time renderer does not reserve room for them, so they overlap. Put details in the page text.
- ER diagrams are always drawn left to right, so their width is the number of columns. Keep two or three: owner edges (`owner_id` to `User`) are not drawn, and a relationship is drawn only in the diagram of the entity that holds the key. Entities from other diagrams appear as plain boxes.
- External packages are not drawn. The package index in `CLAUDE.md` lists them.

## Keep them up to date

The diagrams must always match the code. Each snippet starts with a `%% KEEP UP TO DATE` comment that says what it describes.

- **Data model:** update the `erd-<area>-<part>.md` snippets of [the data model](erd.md) in the same change that adds, removes or renames an EF Core entity, column or relationship. Put the entity in the diagram of its area, and add table names, indexes and constraints to the table at the end of `erd.md` and its translation.
- **Packages:** update `packages.md` in the same change that adds, removes or renames a project in `src/` or changes a project reference, and `packages-runtime.md` when a client or separate service changes how it connects.
- **Flows:** update the voice turn, confirm-tier and task snippets, English and German, in the same change that alters the behavior they show.

## Preview

- `npx clay-doc@5 dev` draws the diagrams with live reload (Node.js 22 or newer).
- `npx clay-doc@5 check` does not draw diagrams, so it does not catch Mermaid syntax errors. Look at the page in the dev server, or paste the block into the [Mermaid live editor](https://mermaid.live).
- Editors: the Markdown preview of VS Code (with a Mermaid extension) and JetBrains IDEs draws `mermaid` blocks.
