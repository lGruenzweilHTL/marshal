# `agent`

Limits of the agent loop and the advisor. Changes need a restart.

## Example

```yaml
agent:
  maxSteps: 8
  voiceLatencyCap: "00:00:08"
  textLatencyCap: "00:01:00"
  maxDelegationDepth: 3
  advisor:
    maxCallsPerRun: 2
    dailyCostCap: 1.0
```

## Keys

| Key | Type | Default | Description |
|---|---|---|---|
| `agent.maxSteps` | integer | `8` | Maximum tool rounds per run. Templates can set their own. Range 1 to 50. |
| `agent.voiceLatencyCap` | duration | `00:00:08` | Hard latency cap for voice turns. |
| `agent.textLatencyCap` | duration | `00:01:00` | Hard latency cap for text turns. |
| `agent.maxDelegationDepth` | integer | `3` | Deepest subagent level. `agent.delegate` is hidden at this depth. Range 0 to 10. |
| `agent.advisor.maxCallsPerRun` | integer | `2` | Calls of `agent.ask_advisor` per run. Templates can override it. Range 0 to 10. |
| `agent.advisor.dailyCostCap` | decimal | `1` | Advisor cost per owner and local day. The tool is hidden once reached. Range 0 to 1000. |

`agent.ask_advisor` is also hidden in voice turns and when no `advisor` tier model is configured.

## Validation

- `agent.maxSteps must be between 1 and 50.`
- `agent.maxDelegationDepth must be between 0 and 10.`
- `agent.advisor.maxCallsPerRun must be between 0 and 10.`
- `agent.advisor.dailyCostCap must be between 0 and 1000.`

The latency caps are not validated.

## Related

- [models](models.md)
- [Agent loop](../../concepts/agent-loop.md)
- [Agent templates](../../guides/agent-templates.md)
