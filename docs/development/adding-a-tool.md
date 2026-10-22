---
icon: fa-solid fa-hammer
description: "Add a model-facing tool to an existing tool source."
---

# Add a tool

Add a model-facing tool to an existing tool source.

## Before you start

- The change is approved and the design states the tool's risk tier and when it is hidden ([OpenSpec workflow](openspec-workflow.md)).
- The capability is allowed. Marshal has no mail send path, no raw G-code and no tool that fetches a caller-chosen URL. Do not write a tool and then forbid it.

## How tools are built

A tool implements `ITool` (`src/Marshal.Abstractions/Tools/ITool.cs`). A group of tools lives in an `IToolSource`. The source returns its tools from `GetToolsAsync`; the tool registry merges all sources.

| Member | Purpose |
|---|---|
| `Descriptor` | Id, description, JSON schema of the parameters, default tier, flags. |
| `IsAvailableAsync(context, ct)` | Tool-specific visibility. Checked before every model call and again by the guard. Defaults to `true`. |
| `Validate(arguments)` | Argument checks that must hold before any tier applies, such as an allowlist. A failure denies the call without a confirmation. |
| `Summarize(arguments, language)` | Short text for confirmation prompts. |
| `InvokeAsync(invocation, ct)` | Runs the call and returns `Result<ToolOutput>`. |

## Steps

::: steps
1. **Write the failing tests**

   Cover the happy path, invalid arguments, the tier, visibility, and the owner. Use `FakeTool` and `FakeToolSource` from `tests/Marshal.Testing` when the test is about the registry or guard, and the fake of your integration's client when it is about the tool. Never call a real model.

2. **Define the descriptor**

   ```csharp
   private static ToolDescriptor Descriptor(string name, string description, string schema) => new()
   {
       Id = new ToolId("infra", name),
       Description = description,
       ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
       DefaultTier = RiskTier.Safe,
   };
   ```

   Rules:

   - The internal id is `<namespace>.<snake_case_name>`. The model sees `<namespace>_<name>`.
   - Risk rules, satellite policies and template allowlists refer to the internal id.
   - Write the description for the model. Say what the tool returns and when to use it.
   - Keep the schema strict. Take ids of known items, never free-form commands or URLs.

3. **Choose the tier**

   `safe` runs immediately. `confirm` is frozen server-side and needs a user action. `forbidden` is removed from the tool list. Pick the default tier in the descriptor and add arg-aware rules to `risk.yaml` when the tier depends on the target (for example `ha.*` against locks). When a call resolves to several targets, the highest tier wins. An unresolvable target falls to confirm.

4. **Implement visibility and validation**

   Override `IsAvailableAsync` when the tool depends on context, such as voice turns, the satellite or the template. Use `Validate` for allowlists. The guard re-runs both; never rely on visibility alone.

5. **Implement the call**

   Return a failed `Result` with a stable error code (`infra.invalid_arguments`) for expected failures. The executor turns it into a structured message for the model. Throw only for bugs.

   Treat text from outside as untrusted. Output that carries mail, feed, note or web content must be marked untrusted so the run is tainted. In a tainted run, memory and task writes escalate to confirm tier.

6. **Register and document**

   Add the tool to the source's tool list. Then:

   - Add rows to the [tool reference](../reference/tools.md).
   - Update the guide for the integration and `deploy/config.example/risk.yaml` if you added rules.
:::

## Check that it works

- The tool appears in the list for a context that may use it and not for one that may not.
- A direct call that bypasses the list is still refused by the guard.
- A confirm-tier call is stored frozen and executes exactly the stored arguments.

## Related

::: cards
- [Adding an integration](adding-an-integration.md)
- [Tool visibility](../concepts/tool-visibility.md)
- [Risk tiers](../reference/risk-tiers.md)
:::
