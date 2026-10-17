# Proposal

## Why

Marshal's memory knows what is true now, but not since when or until when. "Which nozzle did I use before the 0.6?" or "where did I work in 2025?" have no answer, and a fact that stopped being true is either deleted or stays in every prompt. Temporal knowledge graphs (Zep/Graphiti: separate valid time and record time) answer such questions and improved LongMemEval accuracy by up to 18.5%. Memory also only grows: open contradictions and near-duplicates wait in the audit until the user finds them. Idle-time work ("sleep-time compute", Letta: up to 18% better accuracy and 2.5× lower cost per query) can prepare that work and precompute context while nobody is talking to Marshal.

## What Changes

- **Valid time on memory:** chunks and their versions get `validFrom` and `validTo` (when the fact was true in the world), next to the existing timestamps (when Marshal stored it). `memory.remember` and the summarizer may set them ("since March"); `validFrom` defaults to the creation time. Superseding a chunk or updating it in place closes the previous fact's valid time. The memory view edits them.
- Automatic retrieval returns only currently valid chunks. `memory.recall` gains `asOf` to search what was valid at a past time, including closed facts and earlier versions.
- **Idle-time jobs** run after the nightly consolidation:
  - contradiction proposals: for each open `contradicts` pair, a suggestion which fact is current, with valid times;
  - duplicate proposals: near-duplicate chunks of the same owner, type and scope, proposed for merging;
  - project summaries: a short summary per project with recent activity, injected for active projects;
  - task preparation: agent tasks may declare a preparation run at an earlier time that writes notes the main run reads (the briefing uses it to read calendars and news before 06:00).
- Proposals appear in the memory audit; the user accepts (Marshal applies the change) or dismisses. Nothing changes memory without the user.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `memory-store`: valid time on chunks and versions; supersede and in-place update close valid time.
- `memory-retrieval`: automatic retrieval excludes closed facts; `memory.recall` gains `asOf`.
- `memory-consolidation`: `memory.remember` and summarizer candidates carry valid time; the audit holds proposals.
- `system-tasks`: idle-time jobs after nightly consolidation.
- `project-activation`: project summaries injected for active projects.
- `task-system`: task preparation runs.
- `memory-view`: valid time editing, proposals, merge.

## Non-goals

- A full temporal knowledge graph with extracted entities and relations.
- Applying proposals automatically.
- Gating idle-time jobs behind the "help Marshal learn" toggle (they produce no notifications).
- Idle detection from presence; the jobs run on the nightly schedule.

## Impact

- `src/Marshal.Memory`: valid time handling, `asOf` search over chunks and versions, proposal jobs, project summaries.
- `src/Marshal.Data`: columns on `memory_chunks`, `chunk_versions`, `projects`, `tasks`; new `memory_proposals` table; migration; `erd.puml`.
- `src/Marshal.Tasks`: preparation runs and their scheduling; the nightly job chain.
- `web/`: memory editor, history and audit views.
- `deploy/config.example/tasks/briefing.yaml`: preparation example.
