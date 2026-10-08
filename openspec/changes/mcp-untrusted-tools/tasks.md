# Tasks

## 1. Config model and validation

- [ ] 1.1 Write failing `YamlMcpServerCatalog` tests: `untrustedTools` parses into the definition; `untrusted: true` plus `untrustedTools` rejects that server with an error naming both keys while other servers load; an empty glob is rejected; a file without the key has an empty list. Verify they fail.
- [ ] 1.2 Add `UntrustedTools` to `McpServerFile` and `McpServerDefinition`, with the catalog validation. Verify the 1.1 tests pass.

## 2. Per-tool taint

- [ ] 2.1 Write failing `McpToolSource` tests with an in-process fake MCP server reporting `todo__get_items` and `intent__HassTurnOn`: with `untrustedTools: [todo__get_items]` only the first has `UntrustedOutput` and `TaintKind = mcp:ha`; a glob `*get_items` matches the same; `untrusted: true` still marks both; a glob matching nothing leaves the server up with a warning naming the glob. Verify they fail.
- [ ] 2.2 Implement the matching in `Describe` and the warnings on the server state. Verify the 2.1 tests pass.
- [ ] 2.3 Write a failing test that changing `untrustedTools` makes `SameDefinition` false and the next turn's descriptors reflect the new list. Update `SameDefinition`. Verify it passes.
- [ ] 2.4 Write a failing agent-loop test with a fake `IChatClient`: a run that calls the untrusted `ha_todo__get_items` is tainted with source `mcp:ha` and its result is wrapped as untrusted data; a run that only calls `ha_intent__HassTurnOn` is not tainted. Verify it passes.

## 3. Config API, view and example

- [ ] 3.1 Write a failing `Marshal.Api.Tests` test: the config endpoint returns `untrustedTools` and `warnings` per MCP server. Add the fields (additive, v1). Verify it passes and the OpenAPI document shows them.
- [ ] 3.2 Regenerate the SPA API types (`npm run gen:api`). Write a failing `ConfigView` test that the untrusted column shows "all", "some (1)" and "no" and that a server warning is shown, in German and English. Implement it. Verify `npm test` and `npm run build` pass.
- [ ] 3.3 Add `untrustedTools: ["todo__get_items"]` with an explaining comment to `deploy/config.example/mcp/ha.yaml`. Verify the example-config test (config.example loads without errors) passes.
- [ ] 3.4 Check `/health` shows the unmatched-glob warning (extend the existing health test). Verify it passes.

## 4. Integration check

- [ ] 4.1 Run the full .NET suite (`dotnet test`), the web suite (`npm test`, `npm run build`) and `openspec validate mcp-untrusted-tools --strict`. Verify all pass.
