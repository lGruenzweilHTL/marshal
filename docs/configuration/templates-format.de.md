---
icon: fa-solid fa-file-lines
description: "Ein Template ist eine Markdown-Datei in /config/templates/, die festlegt, was ein Durchlauf sehen und tun darf: Modellstufe, Allowlist der Werkzeuge, Skills, Grenzen und ein zusätzlicher Prompt."
---

# Agent-Templates (`templates/*.md`)

Ein Template ist eine Markdown-Datei in `/config/templates/`, die festlegt, was ein Durchlauf sehen und tun darf: Modellstufe, Allowlist der Werkzeuge, Skills, Grenzen und ein zusätzlicher Prompt. Marshal lädt Templates ohne Neustart neu.

Turns von Unterhaltungen nutzen das eingebaute Template `default` (jedes Werkzeug, jeder Skill, kein zusätzlicher Prompt). Eine Datei `default.md` ersetzt es. Aufgaben nennen ein Template in ihrem Feld `template`; `agent_delegate(template, task)` startet damit einen Subagenten.

## Eingebaute Templates

| Name | Genutzt für | Eingebaute Einstellungen | Ersetzen mit |
|---|---|---|---|
| `default` | Turns von Unterhaltungen | jedes Werkzeug, jeder Skill, kein zusätzlicher Prompt | `default.md` |
| `ai_task` | [AI-Tasks](../home-assistant-component/ai-tasks.md) aus Automationen in Home Assistant | `modelTier: fast`, keine Werkzeuge, Prompt: "You run inside a home automation. Answer the instructions; do not ask questions; nobody can answer them." | `ai_task.md` |

`agent_delegate` bietet nie die eingebauten Templates oder die Dateien an, die sie ersetzen. Durchläufe von AI-Tasks sind nicht interaktiv: Ein Werkzeug der Bestätigungsstufe, das du in `ai_task.md` erlaubst, läuft während der Aufgabe nicht. Der Aufruf wartet in der Freigabe-Warteschlange und läuft nur, wenn du ihn freigibst. `deploy/config.example/templates/ai_task.md` ist ein Beispiel mit rein lesenden Werkzeugen.

## Beispiel

```markdown
---
name: house-inspector
description: "Reads the current state of the house and reports what needs attention. Cannot change anything."
modelTier: fast
tools: ["ha.homeassistant__GetLiveContext", "agent.load_skill"]
skills: [house-status]
maxSteps: 4
maxAdvisorCalls: 0
---

You inspect the house on behalf of another agent. Load the house-status skill and follow it.
```

## Frontmatter

Schlüssel sind camelCase. Unbekannte Schlüssel sind Fehler.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `name` | Zeichenkette | Pflicht | Kleinbuchstaben, Ziffern, Bindestriche und Unterstriche (nicht am Anfang oder Ende). Muss dem Dateinamen ohne `.md` gleichen. |
| `description` | Zeichenkette | Pflicht | Wann das Template zu nutzen ist. Wird Aufrufern von `agent_delegate` gezeigt. |
| `modelTier` | `fast` oder `standard` | nach Eingabemodus (Sprache `fast`, sonst `standard`) | Modellstufe der Durchläufe. |
| `tools` | Liste von Globs | `[]` | Allowlist von Globs auf Werkzeug-IDs (zum Beispiel `printer.*`, `agent.load_skill`). Eine leere Liste bedeutet keine Werkzeuge. |
| `skills` | Liste von Namen | alle Skills | Skills, die Durchläufen zur Verfügung stehen. `[]` bedeutet keine. |
| `maxSteps` | Ganzzahl 1 bis 50 | `agent.maxSteps` | Schrittlimit pro Durchlauf. |
| `maxAdvisorCalls` | Ganzzahl 0 bis 10 | `agent.advisor.maxCallsPerRun` | Aufrufe des Advisors pro Durchlauf. |
| `minWakeupInterval` | Dauer | `tasks.minWakeupInterval` | Kürzeste Zeit zwischen einem Aufgabenlauf und seinem Aufwecken; mindestens `00:01:00`. |

Der Body wird dem System-Prompt hinzugefügt.

## Regeln für Subagenten

In einem Subagenten sind Werkzeuge der Bestätigungsstufe verborgen, und der Guard verweigert jeden Aufruf, der zu `confirm` wird, mit dem Hinweis an den Subagenten, die nötige Aktion an seinen Aufrufer zu melden. Subagenten nutzen Eigentümer, Satellitenrichtlinie und Belastung des Aufrufers und geben die Belastung zurück. Die Tiefe der Delegation ist durch `agent.maxDelegationDepth` begrenzt (standardmäßig 3). An Templates mit leerer Liste `tools` kann trotzdem delegiert werden.

## Validierung

Eine abgelehnte Datei wird mit einem Fehler übersprungen; andere Templates laden.

- `<file>: has no YAML frontmatter (--- name / description / tools ---).`
- `<file>: name '<x>' must be lowercase letters, digits, hyphens and underscores.`
- `<file>: name '<x>' must match the file name '<file-name>'.`
- `<file>: description is required.`
- `<file>: modelTier '<x>' must be 'fast' or 'standard'.`
- `<file>: maxSteps must be between 1 and 50.`
- `<file>: maxAdvisorCalls must be between 0 and 10.`
- `<file>: minWakeupInterval must be at least 00:01:00.`
- `<file>: invalid frontmatter at line <n>: ...`
- Ein wörtliches Secret im Frontmatter oder Body.

## Verwandte Seiten

::: cards
- [Anleitung zu Agent-Templates](../guides/agent-templates.md)
- [Format von Skills](skills-format.md)
- [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md)
:::
