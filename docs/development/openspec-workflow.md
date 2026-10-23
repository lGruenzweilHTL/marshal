---
icon: fa-solid fa-diagram-next
description: "Marshal's rules for OpenSpec changes: what each artifact must contain and what a change updates besides code."
---

# OpenSpec workflow

Marshal's rules for OpenSpec changes. Use the `opsx:*` skills (`propose`, `explore`, `apply`, `sync`, `archive`) as usual.

## Rules

- `openspec/specs/` is authoritative. When code and spec disagree, fix the code or propose a spec change; never diverge silently.
- No implementation without an approved change in `openspec/changes/` that covers it.

## Artifact rules

`openspec/config.yaml` adds these to every change:

| Artifact | Must contain |
|---|---|
| Proposal | A "Non-goals" section. |
| Design | Every new interface and its v1 implementations. For every new model-facing tool: its risk tier (`safe`, `confirm`, `forbidden`, including arg-aware rules) and when it is hidden from the model. |
| Tasks | Test-first order: each implementation task follows, or is paired with, the task that writes its failing test. |

## Done means

- Every requirement in the delta specs has a scenario and a test.
- The same change updates `CLAUDE.md` (repository map, package index), the [diagrams](../diagrams/README.md), the docs pages for the changed behavior ([docs style](docs-style.md)) and `Marshal.Demo` when it implements a changed interface.
- The change is synced and archived, so `openspec/specs/` reflects what shipped.

## Related

::: cards
- [Adding an integration](adding-an-integration.md)
- [Adding a tool](adding-a-tool.md)
:::
