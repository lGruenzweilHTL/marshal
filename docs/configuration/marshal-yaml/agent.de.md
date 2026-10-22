---
icon: fa-solid fa-robot
description: "Grenzen der Agentenschleife und des Advisors."
---

# `agent`

Grenzen der Agentenschleife und des Advisors. Änderungen brauchen einen Neustart.

## Beispiel

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

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `agent.maxSteps` | Ganzzahl | `8` | Höchstzahl der Werkzeugrunden pro Durchlauf. Templates können einen eigenen Wert setzen. Bereich 1 bis 50. |
| `agent.voiceLatencyCap` | Dauer | `00:00:08` | Feste Latenzgrenze für Sprach-Turns. |
| `agent.textLatencyCap` | Dauer | `00:01:00` | Feste Latenzgrenze für Text-Turns. |
| `agent.maxDelegationDepth` | Ganzzahl | `3` | Tiefste Ebene von Subagenten. In dieser Tiefe ist `agent.delegate` verborgen. Bereich 0 bis 10. |
| `agent.advisor.maxCallsPerRun` | Ganzzahl | `2` | Aufrufe von `agent.ask_advisor` pro Durchlauf. Templates können das überschreiben. Bereich 0 bis 10. |
| `agent.advisor.dailyCostCap` | Dezimalzahl | `1` | Kosten des Advisors pro Eigentümer und lokalem Tag. Ist die Grenze erreicht, wird das Werkzeug verborgen. Bereich 0 bis 1000. |

`agent.ask_advisor` ist auch in Sprach-Turns verborgen und wenn kein Modell der Stufe `advisor` konfiguriert ist.

## Validierung

- `agent.maxSteps must be between 1 and 50.`
- `agent.maxDelegationDepth must be between 0 and 10.`
- `agent.advisor.maxCallsPerRun must be between 0 and 10.`
- `agent.advisor.dailyCostCap must be between 0 and 1000.`

Die Latenzgrenzen werden nicht validiert.

## Verwandte Seiten

::: cards
- [`models`](models.md)
- [Agentenschleife](../../concepts/agent-loop.md)
- [Agent-Templates](../../guides/agent-templates.md)
:::
