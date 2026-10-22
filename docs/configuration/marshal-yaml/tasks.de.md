---
icon: fa-solid fa-list-check
description: "Worker-Pool, Budgets und Grenzen von Hintergrundaufgaben."
---

# `tasks`

Worker-Pool, Budgets und Grenzen von Hintergrundaufgaben.

## Beispiel

```yaml
tasks:
  workers: 2
  approvalTtl: "00:10:00"
  dailyCostCap: 2.0
  defaultBudget:
    cost: 0.5
    steps: 60
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `tasks.workers` | Ganzzahl | `2` | Aufgabenläufe, die gleichzeitig laufen. Fällige Aufgaben darüber hinaus warten in der Reihenfolge ihrer Laufzeit. Bereich 1 bis 16. |
| `tasks.approvalTtl` | Dauer | `00:10:00` | So lange wartet eine Freigabe im Hintergrund; danach läuft sie ab und zählt als abgelehnt. Mindestens eine Minute. |
| `tasks.dailyCostCap` | Dezimalzahl | `2` | Kosten aller Hintergrund-Durchläufe, [AI-Tasks](ai-tasks.md) und Durchläufe des [MCP-Servers](../../guides/integrations/mcp-clients.md) von Marshal eines Eigentümers pro lokalem Tag. Ist die Grenze erreicht, startet kein neuer Durchlauf. Nicht negativ. |
| `tasks.maxDepth` | Ganzzahl | `3` | Tiefste Kindaufgabe (eine Aufgabe auf oberster Ebene hat Tiefe 0). Bereich 0 bis 10. |
| `tasks.maxActiveChildren` | Ganzzahl | `5` | Unfertige Kindaufgaben, die eine Aufgabe gleichzeitig haben darf. Nicht negativ. |
| `tasks.minWakeupInterval` | Dauer | `00:05:00` | Kürzeste Weckverzögerung und cron-Periode. Templates können eigene setzen. Mindestens eine Minute. |
| `tasks.defaultBudget.tokens` | Ganzzahl | nicht gesetzt (unbegrenzt) | Eingabe- plus Ausgabe-Tokens pro Aktivierung einer Aufgabe. |
| `tasks.defaultBudget.cost` | Dezimalzahl | `0.5` | Kosten pro Aktivierung einer Aufgabe. |
| `tasks.defaultBudget.steps` | Ganzzahl | `60` | Modellaufrufe pro Aktivierung einer Aufgabe. |
| `tasks.defaultBudget.wallTime` | Dauer | `00:30:00` | Laufzeit pro Aktivierung einer Aufgabe. |
| `tasks.defaultBudget.runs` | Ganzzahl | `20` | Durchläufe pro Aktivierung einer Aufgabe. |

`defaultBudget` gilt für Aufgaben, die kein eigenes Budget setzen. Eine Aktivierung reicht von ihrem Auslöser über ihre Weckvorgänge, bis die Aufgabe fertig ist. Kindaufgaben zehren vom Budget der Elternaufgabe. Ein aufgebrauchtes Budget lässt die Aufgabe mit dem Grund `budget_exceeded` scheitern.

## Validierung

- `tasks.workers must be between 1 and 16.`
- `tasks.maxDepth must be between 0 and 10.`
- `tasks.maxActiveChildren cannot be negative.`
- `tasks.minWakeupInterval must be at least one minute.`
- `tasks.dailyCostCap cannot be negative.`
- `tasks.approvalTtl must be at least one minute.`

`defaultBudget` wird nicht validiert.

## Verwandte Seiten

::: cards
- [`maintenance`](maintenance.md)
- [Geplante Aufgaben](../../guides/tasks/scheduled-tasks.md)
- [Freigaben](../../guides/tasks/approvals.md)
- [Format von Aufgaben](../tasks-format.md)
:::
