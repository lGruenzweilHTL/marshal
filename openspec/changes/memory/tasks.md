## 1. Project and storage

- [x] 1.1 Create `Marshal.Memory` and `Marshal.Memory.Tests`; update `packages.puml` and `CLAUDE.md` repository map
- [x] 1.2 Write tests: `sqlite-vec` loads on every connection; health error when it does not
- [x] 1.3 Implement the connection interceptor and ship the native extension in the Docker image
- [x] 1.4 Write deterministic id tests: normalization, owner / scope / project / type separation, project rename keeps ids
- [x] 1.5 Implement `ChunkId` generation
- [x] 1.6 Write store tests: validation (project scope needs project), same-id update creates `ChunkVersion`, identity change creates `supersedes`, no self-supersede, soft delete, FTS and vector index updated in the same transaction, ownership with a second user
- [x] 1.7 Implement entities, migration (incl. FTS5 and vec virtual tables) and `SqliteMemoryStore`; align `erd.puml` and re-render
- [x] 1.8 Write export tests: NDJSON format, round trip, embeddings excluded and recomputed
- [x] 1.9 Implement `NdjsonMemoryExporter`

## 2. Retrieval

- [x] 2.1 Write scorer tests: similarity × importance × freshness, per-type half-life, quotas, token budget, project boost / penalty
- [x] 2.2 Implement `ChunkScorer`
- [x] 2.3 Write retriever tests with deterministic fake embeddings: vector + FTS seeds, edge expansion, superseded replaced, soft-deleted excluded, pinned always included
- [x] 2.4 Implement `HybridMemoryRetriever`, `PinnedMemoryContributor`, `RetrievedMemoryContributor` (single turn embedding shared with activation)
- [x] 2.5 Write `memory.recall` tests (type / project filters, larger budget)
- [x] 2.6 Implement `memory.recall`

## 3. Project activation

- [x] 3.1 Write activator tests: name / alias match, centroid tiers, negative examples, recency prior, stickiness decay, topic shift drop
- [x] 3.2 Implement `ProjectActivator` and centroid maintenance
- [x] 3.3 Write correction tests: rejected / switched / confirmed feedback rows, alias candidate promotion after 3 confirmations
- [x] 3.4 Implement `memory.correct_project` and alias learning
- [x] 3.5 Write test: active projects and reasons recorded in the context-build step
- [x] 3.6 Record activation details in the run recorder

## 4. Writes and consolidation

- [ ] 4.1 Write `memory.remember` tests: clean run writes, tainted run pending with `taint` reason naming mail, cannot pin, upsert by id
- [ ] 4.2 Implement `memory.remember`
- [ ] 4.3 Write summarizer tests with a fake client: input has only user turns and replies, candidates upserted, contradictions create edges, never pinned
- [ ] 4.4 Implement `LlmMemorySummarizer` on session close
- [ ] 4.5 Write audit tests: stale, contradictions, orphans
- [ ] 4.6 Implement `MemoryAudit`

## 5. API and UI

- [ ] 5.1 Write API tests: CRUD, pin / unpin (UI-only path), supersede, soft delete, history, audit, export, projects and aliases, audit-log entries, ownership
- [ ] 5.2 Implement memory endpoints
- [ ] 5.3 Write component tests for list, editor, history, audit, projects views
- [ ] 5.4 Implement list, editor, history, audit and projects views
- [ ] 5.5 Implement force graph and PCA scatter views (PCA computed server-side)
- [ ] 5.6 Show active projects and retrieved chunks in the run detail view
