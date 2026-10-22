---
title: Maintaining the diagrams
icon: fa-solid fa-pen-ruler
description: "Where the Mermaid diagrams live, when to update them and how to preview them."
---

# Maintaining the diagrams

The diagrams are [Mermaid](https://mermaid.js.org) code blocks in the Markdown pages. Clay draws them on the site; GitHub draws them when it shows the file.

| Diagram | Page |
|---|---|
| Packages | [Architecture](../concepts/architecture.md#the-parts) |
| Voice turn | [Architecture](../concepts/architecture.md#how-a-request-moves-through-it) |
| Confirm-tier action | [Risk and confirmation](../concepts/risk-and-confirmation.md#frozen-pending-actions) |
| Task lifecycle | [Tasks and scheduling](../concepts/tasks-and-scheduling.md#the-lifecycle-of-a-run) |
| Data model, one diagram per area | [Data model](erd.md) |

## Keep them up to date

The diagrams must always match the code. Each block starts with a `%% KEEP UP TO DATE` comment that says what it describes.

- **Data model:** update [`erd.md`](erd.md) in the same change that adds, removes or renames an EF Core entity, column or relationship. Put the entity in the diagram of its area, and add table names, indexes and constraints to the table at the end of the page.
- **Packages:** update the diagram in [architecture](../concepts/architecture.md) in the same change that adds, removes or renames a project in `src/`, `web/` or `ha/`, or changes a project reference or key external dependency.
- **Flows:** update the voice turn, confirm-tier and task lifecycle diagrams in the same change that alters the behavior they show.

## Preview

- `npx clay-doc@5 dev` draws the diagrams with live reload (Node.js 22 or newer).
- `npx clay-doc@5 check` does not draw diagrams, so it does not catch Mermaid syntax errors. Look at the page in the dev server, or paste the block into the [Mermaid live editor](https://mermaid.live).
- Editors: the Markdown preview of VS Code (with a Mermaid extension) and JetBrains IDEs draws `mermaid` blocks.
