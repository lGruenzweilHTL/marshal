# Tasks

## 1. Project and client

- [ ] 1.1 Create `src/Marshal.Integrations.Proxmox` and `tests/Marshal.Integrations.Proxmox.Tests`, add both to `Marshal.sln`, reference the project from `Marshal.Api`; verify `dotnet build` succeeds
- [ ] 1.2 Write failing tests for `ProxmoxOptions` validation (https URL, token id pattern, fingerprint normalization, required secret); implement and register in the configuration stage; verify with the Proxmox tests
- [ ] 1.3 Write failing tests for `ProxmoxRestClient` against an in-process fake PVE server (token header format, `cluster/resources` and `cluster/tasks` parsing, 401 and connection errors → `proxmox.unavailable`, only `GET` sent); implement `IProxmoxClient`; verify tests pass
- [ ] 1.4 Write a failing test for certificate pinning (matching fingerprint accepted, other fingerprint rejected) using a self-signed test certificate on the fake server; implement the validation callback; verify the test passes

## 2. Tools

- [ ] 2.1 Write failing tests for `proxmox.overview`, `proxmox.guests` (node and status filters, templates excluded), `proxmox.storage` and `proxmox.tasks` (`failedOnly`, `since`, limit 50) with a fake `IProxmoxClient`; implement `ProxmoxToolSource`; verify tests pass
- [ ] 2.2 Write a failing test that no Proxmox tools are registered without a `proxmox` section and that tool descriptors are safe and not untrusted; implement the registration; verify with `dotnet test --project tests/Marshal.Api.Tests`

## 3. Webhook source

- [ ] 3.1 Add JSON fixtures for the documented body template (vzdump error, vzdump info, replication, fencing, package-updates, system-mail, extra title/message keys, invalid); write failing `ProxmoxNormalizer` tests from the spec scenarios; implement the normalizer (trusted); verify with `dotnet test --project tests/Marshal.Tasks.Tests`
- [ ] 3.2 Add `ApiScopes.HooksProxmox`, register the normalizer, and write failing API tests (202 + event for `hooks:proxmox`, 403 for `hooks:uptime-kuma` token, admin role gets the scope, CLI token creation); verify with `dotnet test --project tests/Marshal.Api.Tests`

## 4. Documentation

- [ ] 4.1 Add the `proxmox` section and the PVE webhook target setup (header, body template, token scope, `PVEAuditor` role) to `deploy/config.example/marshal.yaml`; verify `ExampleConfigTests` passes
- [ ] 4.2 Update the CLAUDE.md repo map and `docs/diagrams/packages.puml` (new project, reference from `Marshal.Api`), re-render the SVG; verify the SVG renders

## 5. Integration checks

- [ ] 5.1 Run the full .NET test suite and `openspec validate proxmox --strict`; both pass
