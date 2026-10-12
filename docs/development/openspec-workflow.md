# OpenSpec workflow

Propose, implement, sync and archive a change. Marshal is spec-driven: behavior is defined in `openspec/specs/` before it is built.

## Rules

- `openspec/specs/` is authoritative. If code and a spec disagree, the spec wins. Fix the code or propose a spec change; never diverge silently.
- Do not start implementation without an approved change in `openspec/changes/` that covers it.
- `docs/internal/` holds informal design notes. A spec supersedes them once a topic exists in `openspec/specs/`.

## Layout

| Path | Holds |
|---|---|
| `openspec/specs/<capability>/spec.md` | The current requirements and scenarios. |
| `openspec/changes/<change>/` | A proposed or in-progress change: proposal, design, delta specs, tasks. |
| `openspec/changes/archive/` | Finished changes. |
| `openspec/config.yaml` | Project context and the rules each artifact must follow. |

## Steps

### 1. Propose

Use the `opsx:propose` skill (or the `openspec-propose` skill) to create the change and generate its artifacts in one step. `openspec/config.yaml` adds these rules:

- The proposal has a "Non-goals" section.
- The design names every new interface and its v1 implementations.
- For every new model-facing tool, the design states its risk tier (safe, confirm or forbidden, including arg-aware rules) and when the tool is hidden from the model.
- Tasks are test-first: each implementation task is preceded by, or paired with, the task that writes its failing test.

Use `opsx:explore` to think a problem through before you propose.

### 2. Apply

Use `opsx:apply` to work through the tasks. Write the failing test, make it pass, then tick the task. Update `CLAUDE.md` (repository map, package index), the diagrams and the documentation in the same change when the change calls for it.

### 3. Sync

Use `opsx:sync` to merge the change's delta specs into `openspec/specs/` without archiving the change.

### 4. Archive

Use `opsx:archive` when the implementation is complete. It moves the change to `openspec/changes/archive/` and syncs the specs if you have not.

## Check that it works

- Every requirement in the delta specs has a scenario and a test.
- `openspec/specs/` reflects the behavior you shipped.
- The documentation pages for the changed behavior match the spec ([docs style](docs-style.md)).

## Related

- [Development index](index.md)
- [Adding an integration](adding-an-integration.md)
