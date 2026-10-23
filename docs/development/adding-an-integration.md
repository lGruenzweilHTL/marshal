---
icon: fa-solid fa-puzzle-piece
description: "Rules for adding an external system to Marshal: project layout, interface, options, secrets, tools, tests and the files to update."
---

# Add an integration

Rules for adding an external system to Marshal. `src/Marshal.Integrations.Proxmox` is the compact reference: an options class, a client interface with one REST implementation, a tool source and one registration method.

::: prompt For a coding agent
Read CLAUDE.md, STYLE_GUIDE.md and
https://lgruenzweilhtl.github.io/marshal/development/adding-an-integration.md.
Then implement the integration of the OpenSpec change
openspec/changes/<change>/ test first, with the Proxmox integration as the
reference. Verify the external API against its current documentation, keep
every client behind a small interface, and work through the documentation
checklist. Run the tests of the changed
projects and `npx clay-doc@5 check` until both pass.
:::

## Project

- `src/Marshal.Integrations.<Name>` and `tests/Marshal.Integrations.<Name>.Tests`, both in `Marshal.sln`.
- Reference `Marshal.Abstractions` (and `Marshal.Data` only if the integration stores rows). Package versions go in `Directory.Packages.props`.
- Verify the external API against its current documentation, not the design notes.

## Interface and options

- One focused client interface (`IProxmoxClient`). Express differences between implementations with capability flags, not `is` checks. Expected failures (host unreachable, entity not found) return a `Result`.
- A sealed options class bound to a `marshal.yaml` section, validated with `ValidateOnStart()`:

  ```csharp
  services.AddOptions<ProxmoxOptions>().Bind(configuration.GetSection("proxmox"))
      .Validate(options => options.Url is null || options.Url is { IsAbsoluteUri: true, Scheme: "https" }, "proxmox.url must be an absolute https URL.")
      .ValidateOnStart();
  ```

- Options hold secret **names** (`tokenSecret: proxmox_token`); the client resolves values through `ISecretStore`. Mark those properties `[Secret]`, so startup fails on a literal secret in `/config`.

## Registration

- One extension method, `services.AddMarshal<Name>(configuration)`, called from `src/Marshal.Api/Hosting/MarshalApp.cs`.
- HTTP clients through `IHttpClientFactory` with `AddStandardResilienceHandler()`. Retry only calls that are safe to repeat.
- An `IToolSource` that returns **no tools while the integration is unconfigured**. Tools follow [adding a tool](adding-a-tool.md).

## Safety rules

- Every tool has a default tier; arg-aware rules go in `deploy/config.example/risk.yaml`.
- Tools that are forbidden, outside the template allowlist, denied for the satellite, or confirm-tier on a satellite that cannot confirm are removed from the list. The guard checks again.
- Outside content (mail, feeds, notes, web pages) is untrusted: mark tool output untrusted so the run is tainted.
- Owned rows carry `owner_id` and go through the owner-scoped filters ([database migrations](database-migrations.md)). Read the owner from the execution context.

## Tests

- Fake the external system: an in-process `TestHost` server, recorded traffic or a Testcontainers image.
- Cover the unconfigured case (no tools), each tool's tier, and ownership with a second fake user.

## Update in the same change

- [ ] `CLAUDE.md`: repository map, package index rows.
- [ ] The [package diagram](../concepts/architecture.md#the-parts) and, for new entities, the [data model](../diagrams/erd.md).
- [ ] `deploy/config.example/`: the new section.
- [ ] `src/Marshal.Demo`, if you changed an interface it simulates. A new integration is simulated only when a change adds it to the [demo mode](../guides/demo-mode.md) scope.
- [ ] A guide in `docs/guides/integrations/<name>.md` and a section under `docs/configuration/marshal-yaml/`, listed in [`integrations`](../configuration/marshal-yaml/integrations.md).
- [ ] Rows in the [tool reference](../reference/tools.md); rule changes in the [risk tiers reference](../reference/risk-tiers.md); new secrets in the [secrets reference](../configuration/secrets-reference.md); new events or notification sources in their reference pages.
- [ ] A line in `CHANGELOG.md`.

## Check that it works

- Unconfigured: the model sees none of the integration's tools.
- Configured: the tools appear and return data from your fake.
- Startup fails with a clear message on invalid options and on a literal secret.

## Related

::: cards
- [Tool visibility](../concepts/tool-visibility.md)
- [Risk and confirmation](../concepts/risk-and-confirmation.md)
- [Untrusted content](../concepts/untrusted-content.md)
:::
