---
icon: fa-solid fa-hammer
description: "Rules for adding a model-facing tool to an existing tool source."
---

# Add a tool

Rules for adding a model-facing tool to an existing tool source. The change's design names the tool's risk tier and when it is hidden ([OpenSpec workflow](openspec-workflow.md)).

::: prompt For a coding agent
Read CLAUDE.md, STYLE_GUIDE.md and
https://lgruenzweilhtl.github.io/marshal/development/adding-a-tool.md.
Then implement the tool of the OpenSpec change openspec/changes/<change>/
test first: write the failing tests, add the descriptor with the tier the
design names, visibility and validation, and the rows in
docs/reference/tools.md. Never call a real model in tests. Run the tests of
the changed projects and `npx clay-doc@5 check` until both pass.
:::

## The contract

A tool implements `ITool` (`src/Marshal.Abstractions/Tools/ITool.cs`) and lives in an `IToolSource`; the registry merges all sources.

| Member | Purpose |
|---|---|
| `Descriptor` | Id, description, JSON schema of the parameters, default tier, flags. |
| `IsAvailableAsync(context, ct)` | Context-dependent visibility (voice turns, satellite, template). Checked before every model call and again by the guard. |
| `Validate(arguments)` | Checks that hold before any tier applies, such as an allowlist. A failure denies the call without a confirmation. |
| `Summarize(arguments, language)` | Short text for confirmation prompts. |
| `InvokeAsync(invocation, ct)` | Runs the call, returns `Result<ToolOutput>`. |

```csharp
private static ToolDescriptor Descriptor(string name, string description, string schema) => new()
{
    Id = new ToolId("infra", name),
    Description = description,
    ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
    DefaultTier = RiskTier.Safe,
};
```

## Rules

- **Forbidden capabilities get no code.** No mail send, no raw G-code or free-form device commands, no tool that fetches a caller-chosen URL. Never write a tool and then forbid it.
- **Ids.** Internal id `<namespace>.<snake_case_name>`; the model sees `<namespace>_<name>`. Risk rules, satellite policies and template allowlists use the internal id. The namespace names the capability, not the provider.
- **Schema.** Strict. Take ids of known items, never free-form commands or URLs. Write the description for the model: what the tool returns and when to use it.
- **Tier.** Set the default in the descriptor. When the tier depends on the target, add arg-aware rules to `deploy/config.example/risk.yaml`. Several targets: the highest tier wins. An unresolvable target falls to `confirm`.
- **Visibility is not enforcement.** The guard re-runs `IsAvailableAsync` and `Validate`.
- **Failures.** Expected failures return a failed `Result` with a stable code (`infra.invalid_arguments`). Throw only for bugs.
- **Untrusted output.** Output that carries mail, feed, note or web content is marked untrusted, so the run is tainted and its memory and task writes escalate to `confirm`.
- **Tests** cover the happy path, invalid arguments, the tier, visibility and the owner. Use `FakeTool` / `FakeToolSource` for registry and guard tests, the integration's client fake for the tool itself.

## Update in the same change

- Rows in the [tool reference](../reference/tools.md), with tier and visibility.
- The integration's guide, and `deploy/config.example/risk.yaml` plus the [risk tiers reference](../reference/risk-tiers.md) if you added rules.

## Check that it works

- The tool is listed for a context that may use it and not for one that may not.
- A direct call that bypasses the list is still refused by the guard.
- A confirm-tier call is stored frozen and executes exactly the stored arguments.

## Related

::: cards
- [Adding an integration](adding-an-integration.md)
- [Tool visibility](../concepts/tool-visibility.md)
- [Risk tiers](../reference/risk-tiers.md)
:::
