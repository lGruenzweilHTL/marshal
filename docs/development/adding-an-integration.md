---
icon: fa-solid fa-puzzle-piece
description: "Add a new external system to Marshal behind a small interface, with tools, risk tiers, secrets and tests."
---

# Add an integration

Add a new external system to Marshal behind a small interface, with tools, risk tiers, secrets and tests.

## Before you start

- Have an approved OpenSpec change that covers the integration ([OpenSpec workflow](openspec-workflow.md)).
- Verify the external API against its current documentation. Do not rely on the design notes.
- Decide up front whether any capability must never exist (for example mail sending). A forbidden capability gets no code path at all.

The Proxmox integration (`src/Marshal.Integrations.Proxmox`) is a compact reference: an options class, a client interface with one REST implementation, a tool source and one registration method.

## Steps

::: steps
1. **Create the project**

   Create `src/Marshal.Integrations.<Name>` and `tests/Marshal.Integrations.<Name>.Tests`. Add both to `Marshal.sln`. Package versions go in `Directory.Packages.props`, never in the project file. Reference `Marshal.Abstractions`; reference only the packages you need.

2. **Define a small interface**

   Put the capability behind one focused interface (`IProxmoxClient`). Express differences between implementations with capability flags, not `is` checks. Expected failures (host unreachable, entity not found) return a `Result`, not an exception.

3. **Add options with validation**

   Bind a sealed options class to a section of `marshal.yaml` and validate it:

   ```csharp
   services.AddOptions<ProxmoxOptions>().Bind(configuration.GetSection("proxmox"))
       .Validate(options => options.Url is null || options.Url is { IsAbsoluteUri: true, Scheme: "https" }, "proxmox.url must be an absolute https URL.")
       .ValidateOnStart();
   ```

   Config never holds secrets. Options reference a secret by name (`tokenSecret: proxmox_token`) and the client resolves the value through `ISecretStore`. Mark option properties that hold secrets with `[Secret]`; startup then fails when a literal secret appears in `/config`. See [secrets reference](../configuration/secrets-reference.md).

4. **Register the services**

   Expose one extension method, `services.AddMarshal<Name>(configuration)`, and call it from `src/Marshal.Api/Hosting/MarshalApp.cs`. Register HTTP clients through `IHttpClientFactory` with `AddStandardResilienceHandler()`. Retry only when the calls are safe to repeat.

5. **Expose tools**

   Implement `IToolSource` and register it (`services.AddSingleton<IToolSource, ...>()`). Return no tools while the integration is unconfigured, so the model never sees tools that cannot work. [Adding a tool](adding-a-tool.md) covers descriptors, tiers and tests.

   Tool ids are `<namespace>.<snake_case_name>`. The namespace names the capability, not the provider: `printer`, not `moonraker`.

6. **Assign risk tiers**

   Every tool has a default tier: `safe`, `confirm` or `forbidden`. Choose the lowest tier that is still safe, and write arg-aware rules in `deploy/config.example/risk.yaml` when the tier depends on the target or arguments. Rules and visibility:

   - Confirm-tier actions are frozen server-side (tool, arguments, nonce, TTL). Only a user action can confirm.
   - A tool that is forbidden, outside the agent template allowlist, denied for the satellite, or confirm-tier on a satellite that cannot confirm is removed from the tool list. The guard checks the same rules again.
   - Content from the integration that comes from outside (mail, feeds, notes, web pages) is untrusted data. Mark the tool output as untrusted so the run becomes tainted.
   - Never add a tool that fetches a caller-chosen URL.

7. **Scope data by owner**

   Every user-owned row has `owner_id`, and every query goes through the owner-scoped query filters in `Marshal.Data`. Read the current owner from the execution context, never from a global. If the integration stores rows, follow [database migrations](database-migrations.md).

8. **Write the tests first**

   - Fake the external system: an in-process server with `Microsoft.AspNetCore.TestHost`, recorded traffic, or a Testcontainers image.
   - Use the fakes from `tests/Marshal.Testing`. Add new shared fakes there.
   - Test the unconfigured case (no tools), each tool's tier, and ownership with a second fake user.

9. **Update the repository files**

   - `CLAUDE.md`: repository map entry, package index rows for new packages.
   - The package diagram in [architecture](../concepts/architecture.md#the-parts) and, if you added entities, the [data model](../diagrams/erd.md) ([maintaining the diagrams](../diagrams/README.md)).
   - `deploy/config.example/`: example config for the new section.
   - `src/Marshal.Demo`: if you change an interface that demo mode implements (Home Assistant, printers, mail, RSS, notes), update the simulated implementation in the same change. A new integration is not simulated unless an OpenSpec change adds it to the [demo mode](../guides/demo-mode.md) scope.
:::

## Documentation checklist

A new integration is not finished until the docs match:

- [ ] A guide in `docs/guides/integrations/<name>.md` ([docs style](docs-style.md)).
- [ ] A configuration page or section under `docs/configuration/marshal-yaml/`, listed in [`integrations`](../configuration/marshal-yaml/integrations.md).
- [ ] Rows for every tool in the [tool reference](../reference/tools.md), with tier and visibility.
- [ ] Risk tier changes in the [risk tiers reference](../reference/risk-tiers.md) when you add rules.
- [ ] New secrets in the [secrets reference](../configuration/secrets-reference.md).
- [ ] New events or notification sources in the matching reference pages.
- [ ] A line in the changelog ([release process](release-process.md)).

## Check that it works

- With no configuration, the model sees none of the integration's tools.
- With valid configuration, the tools appear and return data from your fake.
- Startup fails with a clear message on invalid options and on a literal secret.

## Related

::: cards
- [Tool visibility](../concepts/tool-visibility.md)
- [Risk and confirmation](../concepts/risk-and-confirmation.md)
- [Untrusted content](../concepts/untrusted-content.md)
:::
