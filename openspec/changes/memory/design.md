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
