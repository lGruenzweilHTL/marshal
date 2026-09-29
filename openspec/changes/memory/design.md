## Context

Sources: brainstorm notes §9 (memory, project activation), §5 (multilingual embeddings, English chunks), §4 (taint, UI-only pinning), §18 view 5, §21 (memory is not a log). granum (github.com/e-reitbauer/granum) is the model; its Python / Kuzu code is not reused. The `core-agent` context builder offers the `IContextContributor` seam; `extensibility` offers templates for the cheap summarizer.

## Goals / Non-Goals

**Goals:**
- Memory that stays useful as the corpus grows: hybrid retrieval, deterministic ids, versions, edges.
- Zero extra model round trips on voice turns (retrieval and activation are server-side).
- Full visibility and control in the UI.

**Non-Goals:**
- Scheduled consolidation / export (background-work), notes import, sharing.

## Decisions

### D1. Storage
Tables `chunks`, `chunk_versions`, `chunk_edges`, `projects`, `project_aliases`, `project_feedback` in the main SQLite DB. FTS5 external-content table over `chunks(title, content)`; `sqlite-vec` virtual table `chunk_vectors(chunk_id, embedding float[N])` with N from the configured embedding model (dimension change → re-embed job on startup). `sqlite-vec` is loaded as a native extension on every connection through an EF Core connection interceptor; the native binary ships in the Docker image.
*Alternative:* separate vector DB (Qdrant) — rejected, second service for a single-user corpus.

### D2. Deterministic id
`ChunkId = base32(sha256(ownerId | scope | projectId | type | normalize(title)))[..26]`; `normalize` = Unicode NFKC, lowercase, collapse whitespace, strip punctuation.

### D3. Retrieval pipeline
```
turn text + area ─▶ embed once ─▶ ┬─ vector top-K (sqlite-vec)
                                   └─ FTS5 top-K (bm25)
         seeds (union, normalized scores) ─▶ BFS over edges (depth 1, relates_to / depends_on)
         ─▶ replace superseded ─▶ score = sim × importance × freshness × projectFactor
         ─▶ per-type quotas + token budget ─▶ MemoryContributor
```
Freshness `= 0.5^(age / halfLife)` with per-type half-life (events short, procedures / devices long). All weights in options; tested with deterministic fake embeddings.

### D4. Project activation
`ProjectActivator` runs before retrieval using the same embedding. Order: explicit (name / active alias, substring on normalized text) → centroid cosine with negative-example penalty → recency prior → confidence tier. Session-level stickiness is stored in the session's in-memory state (decay factor per turn; dropped when consecutive turn similarity < threshold). Centroids are recomputed on project or top-chunk change.

### D5. Summarizer
On session close (hosted queue, not the scheduler), a `fast`-tier call with a built-in `memory-summarizer` template receives the session's user turns and assistant replies and returns structured chunk candidates. Written with `CreatedBy = consolidation`; never pinned.

### D6. Interfaces introduced
| Interface | Purpose | v1 implementation |
|---|---|---|
| `IMemoryStore` | chunk / version / edge CRUD, ids | `SqliteMemoryStore` |
| `IMemoryRetriever` | automatic and deliberate retrieval | `HybridMemoryRetriever` |
| `IChunkScorer` | scoring and quotas | `ChunkScorer` |
| `IProjectActivator` | active projects per turn | `ProjectActivator` |
| `IMemorySummarizer` | session → candidates | `LlmMemorySummarizer` |
| `IMemoryAudit` | stale / conflicts / orphans | `MemoryAudit` |
| `IMemoryExporter` | NDJSON export / import | `NdjsonMemoryExporter` |
| `IContextContributor` (existing) | inject memory | `PinnedMemoryContributor`, `RetrievedMemoryContributor` |

### D7. Model-facing tools
| Tool | Tier | Flags | Hidden when |
|---|---|---|---|
| `memory.remember(type, title, content, importance?, scope?, project?, sourceRef?)` | safe | `persists` (confirm in tainted runs, reason `taint`) | template allowlist; satellite `denyTools` |
| `memory.recall(query, type?, project?, limit?)` | safe | — | template allowlist; satellite `denyTools` |
| `memory.correct_project(kind, project?)` | safe | — | no projects exist |

No tool can pin, delete or edit history; those are UI actions (audited).

## Risks / Trade-offs

- [Silent misses] → hybrid search + `memory.recall`.
- [Stale / contradictory facts] → versions, `contradicts` edges, audit.
- [sqlite-vec native binary per platform] → pinned version in the image; startup check that the extension loads, health error otherwise.
- [Embedding model change breaks vectors] → dimension and model name stored; mismatch triggers re-embedding.
- [Retrieval latency on voice turns] → one local embedding call; budgets tuned so retrieval < 150 ms on the target hardware (measure).

## Migration Plan

EF migration adds memory tables; the FTS and vector virtual tables are created by raw SQL in the migration. No existing data.

## Open Questions

- Default quotas, half-lives and confidence thresholds — start with documented defaults, tune with real use (evaluation stays out of CI).

## Implementation Notes

### sqlite-vec (tasks 1.2, 1.3)

- **Package.** The upstream author publishes the `sqlite-vec` NuGet package (owned by the .NET data team, used in
  Microsoft's docs) with `runtimes/{linux,osx}-{x64,arm64}` and `win-x64` `vec0` binaries; its latest version is
  the prerelease `0.1.7-alpha.2.1` (GitHub releases are at v0.1.9, 2026-03). Third-party repackagings of v0.1.9
  exist but were not used. The package's `contentFiles` source (a `LoadVector` extension method) is excluded.
  Upgrade to a stable package once one is published.
- **Loading.** `UseMarshalSqlite` adds `SqliteVectorExtension.Interceptor`, which calls
  `EnableExtensions` + `LoadExtension("vec0")` whenever EF Core opens a connection (Microsoft.Data.Sqlite finds the
  RID-specific native asset through the host's native search directories). An already open connection (the tests'
  in-memory databases) is loaded directly. A failed load is swallowed so the rest of the database keeps working;
  the `memory` health check probes `vec_version()` on a fresh connection and reports unhealthy with the reason.
- **Image.** `dotnet publish` copies `runtimes/*/native/vec0.*` into `/app`, which the Dockerfile ships as is;
  a published build answered `/health` with `memory: healthy, sqlite-vec v0.1.7-alpha.2.1`.

### Store (tasks 1.4–1.7)

- **Ids.** `ChunkIds.Compute`: SHA-256 over `owner|scope|project|type|normalize(title)` (owner and project as Guid
  "D", scope and type lowercase), first 130 bits as lowercase RFC 4648 base32 (26 characters). `normalize` = NFKC,
  lowercase (invariant), Unicode punctuation removed, whitespace collapsed and trimmed.
- **Keyword index.** `chunks_fts` is a regular FTS5 table with its own copy of title and content plus unindexed
  `chunk_id` / `owner_id`, not an external-content table: `chunks` has a text primary key, and the implicit rowid an
  external-content index would rely on is not stable across `VACUUM`. Tokenizer `unicode61 remove_diacritics 2`.
- **Vector index.** `chunk_vectors` is a vec0 table `(chunk_id TEXT PRIMARY KEY, owner_id TEXT PARTITION KEY,
  embedding float[N] distance_metric=cosine)`; the partition key keeps KNN queries within one owner. N and the
  model id are recorded in `memory_vector_index`. The migration creates `chunks_fts` and `memory_vector_index`; the
  vec0 table is created on the first write with the embedding's length. A write with another model or dimension
  fails; startup rebuilds the index (re-embedding) when the configured model changes.
- **Writes.** Validation and embedding happen before the transaction; unchanged chunks are not re-embedded. Inside
  one transaction: the EF Core rows, then the FTS row and the vector row (raw SQL joined to the transaction; owner
  filters do not apply to raw SQL, so every index query filters by owner explicitly). A same-id write restores a
  soft-deleted chunk. Soft delete removes the index entries. Pinning is never changed by a write. New columns
  beyond the ERD sketch: `chunks.created_by`, `chunks.last_retrieved_at` (for the stale audit),
  `projects.created_at` / `deleted_at`, `project_aliases.created_at`.

### Export (tasks 1.8, 1.9)

- One JSON object per line with `kind` first: `project` lines (project chunks need their project; not listed in the
  spec but required for a lossless round trip), then `chunk`, `version` (with its id, so a re-import skips it) and
  `edge` lines, each sorted (id; chunk id + time; from / to / kind). Enum values in snake_case, times ISO 8601
  round-trip format, no embeddings and no `last_retrieved_at` (runtime statistics).
- Import recomputes chunk ids for the importing owner (identical for the same owner), maps versions and edges,
  skips rows that exist, and re-embeds the imported live chunks in the same transaction as the rows.

### Retrieval (tasks 2.1–2.6)

- **Seeds.** Vector KNN on `chunk_vectors` (k = `seedCount`, partition = owner; similarity = 1 − cosine distance)
  and FTS5 on `chunks_fts` (every word of the query quoted and OR-ed, so user text never becomes FTS syntax;
  similarity = 0.9 × bm25 relative to the best hit). The union keeps the higher similarity per chunk.
- **Expansion.** One breadth-first step from seeds along `relates_to` / `depends_on` in both directions,
  similarity = seed × `neighbourFactor` (0.7). Then every candidate follows `supersedes` edges to its newest live
  successor. Deleted chunks are dropped; pinned chunks are left to the pinned layer (recall includes them).
- **Scoring.** similarity × importance / 5 × 0.5^(age / half-life of the type) × project factor (active 1.5,
  inactive 0.5, general 1); per-type quotas, a token budget estimated at four characters per token, minimum score
  0.05. Selected chunks get `last_retrieved_at` (stale audit).
- **Context.** `PinnedMemoryContributor` (order 150) and `RetrievedMemoryContributor` (order 160). `TurnMemory`
  embeds the turn text (with " (in <area>)" when the turn has an area) once per `ContextRequest` and runs project
  activation with that embedding. `memory.recall` uses the recall budget (2000), type / project filters, pinned
  chunks included and a `limit` (default 10, max 30); a project filter also counts as active for scoring.

### Project activation (tasks 3.1, 3.2)

- **Order.** Name, then active alias: whole-word match on the normalized turn text; both sticky. Then, for the
  other live projects not rejected in this session: centroid score = cosine(turn, centroid) − 0.5 × the highest
  cosine to one of the project's rejected phrases, plus a recency prior of 0.05 halving every 7 days since the
  project was last active. ≥ 0.75 sticky, ≥ 0.6 this turn only; reason `recency` when only the prior crossed
  the threshold. Remaining sticky projects are active with reason `sticky` and their weight as score.
- **Stickiness.** Per session in memory (`ProjectSessions`): weight 1 on a sticky activation, × 0.7 per turn,
  dropped below 0.3; a turn with cosine < 0.3 to the previous turn clears all.
- **Centroid.** Normalized mean of the embedding of "name + description" and the vectors of the 10 most important
  live project chunks; stored in `projects.centroid`, cleared by every write or delete of a project chunk (and by
  description edits), recomputed on the next activation.

### Corrections and alias learning (tasks 3.3, 3.4)

- **Target turn.** A correction arrives in the turn after the inference, so the session keeps the last two turns;
  `memory.correct_project` refers to the previous turn (the current one when there is none) and to the projects
  inferred or sticky in either turn. The phrase is the normalized turn text (max 120 characters), stored with the
  turn embedding.
- **Kinds.** `rejected`: unloads (sticky removed, not inferred again in the session; explicit mentions still
  activate), `Rejected` row per project (negative example). `switched` (project required): the previous projects
  as for `rejected`, a `Switched` row for the new one, which becomes sticky from the next turn and gets an alias
  confirmation. `confirmed`: `Confirmed` row and an alias confirmation.
- **Uncorrected inferences.** When a turn leaves the two-turn window (the following turn could have corrected it)
  and no correction or confirmation handled it, each inferred project counts one confirmation of the phrase. An
  alias candidate becomes active at `aliasPromotion` (3) confirmations; rejected aliases stay rejected.

### Run recording (tasks 3.5, 3.6)

- `ContextRequest` carries a `ContextNotes` collection. Contributors add `ContextNote(Kind, Label, Detail, Score)` entries
  that explain the build but never reach the prompt: one `project` note per active project (reason as detail, score
  for inferred activations) and one `memory` note per pinned or retrieved chunk (type plus `pinned` or the retrieval
  source, rounded score).
- `AgentRunner` copies the notes onto the context step's `RunStepRecord.Notes`; `DbRunRecorder` stores them in the
  step payload JSON (no schema change), and `RunStepDetail.Notes` exposes them to the web run detail.

### Writes and summarization (tasks 4.1–4.4)

- `memory.remember` is safe and flagged `Persists`; the guard's existing taint escalation turns it into a pending action
  with a `taint` reason naming the source. Its schema has no pin argument, and `IMemoryStore.WriteAsync` never pins.
- Session close: `ISessionCloseListener` (Abstractions) is notified by `SessionResolver` and `SessionMaintenance` after
  a session is closed. `SessionSummaryInbox` forgets the session's project activation state and queues it in an
  in-process channel; `SessionSummaryWorker` (hosted service) summarizes one session at a time as its owner. A crash is
  logged and skipped.
- `LlmMemorySummarizer`:
  - Input: `IConversationHistory` (context-step inputs and reply-step contents at depth 0 only, so tool results and
    subagent output never reach it) plus existing memory retrieved for the user turns with `MarkRetrieved: false`, so
    summarization does not keep chunks fresh for the audit. Existing chunks are listed with their ids so the model can
    reuse titles (same id means update in place) and name contradictions.
  - Model: the `memory-summarizer` template from `/config/templates` if present, else the built-in one (fast tier,
    JSON answer `{"candidates":[...]}`). The call is recorded as a run of the session (trigger `memory_summarizer`,
    context and model-call steps, no reply step so it never shows up in the conversation history).
  - Contradictions: a candidate is a contradiction instead of an update when the model names the contradicted chunk
    id, or when the stored chunk with the same id is pinned or was changed after the session began (the stored value
    is newer than the conversation). The candidate is then stored under `<title> (conflicting)` with a `contradicts`
    edge to the stored chunk, which stays unchanged; the audit lists the pair until the user resolves it.
  - Candidates for soft-deleted chunks are skipped (a deleted chunk is never revived by consolidation); an unknown
    project falls back to general scope. Candidates are written with `CreatedBy = consolidation`.

### Audit (tasks 4.5, 4.6)

- `MemoryAudit` reads the owner's chunks, edges and deleted projects once and evaluates in memory (personal corpus).
- Stale: live, unpinned chunks whose last activity (later of update and retrieval) is older than
  `memory.audit.staleAfterDays` (180). Pinned chunks are injected without retrieval, so they never count as stale.
- Open contradictions: `contradicts` edges between two live chunks with no `supersedes` edge between them in either
  direction.
- Orphans: live chunks of soft-deleted projects. Dangling edges: edges from a deleted chunk, or to a deleted chunk
  unless the edge is `supersedes` (the replaced version is history).

### Host wiring

- The embedding model is configured in `marshal.yaml` under `embedding` (`provider` `ollama` or `openai-compatible`,
  `model`, `endpoint`, optional `apiKeySecret`) and bound as `ModelRoutingOptions.Embedding`, so the existing secret
  binding covers its key. It is required: memory runs on every turn. `IEmbeddingGeneratorFactory` (Core) is implemented
  by the host (`ProviderEmbeddingGeneratorFactory`, OllamaSharp / OpenAI SDK); Anthropic offers no embeddings.
- `AddMarshalMemory` (Marshal.Memory) registers the store, retrieval, project activation, the two memory context
  contributors, the three memory tools, the summarizer with its inbox (as `ISessionCloseListener`) and worker, and
  the audit; `Marshal.Api` references `Marshal.Memory` and calls it after the Home Assistant integration. Options bind
  from `memory` in `marshal.yaml`.
- `RetrievedMemoryContributor` catches embedding and retrieval failures: the turn continues without retrieved memory
  (logged, and noted as `memory / unavailable` on the context step), so device control never depends on the
  embedding server. Pinned memory needs no embedding and stays.
- Embedding model change: `MemoryIndexStartup` (hosted service, after the database is migrated) runs
  `MemoryIndexRebuilder` when `memory_vector_index.model` differs from the configured generator's model id. It marks
  the index `rebuild-pending`, recreates the vector table with the new dimension, re-embeds every owner's live chunks
  (per owner, in owner scope, batches of 32) and feedback phrases, clears project centroids, then records the new
  model. An interrupted rebuild (embedding server down) is logged and runs again on the next start.
