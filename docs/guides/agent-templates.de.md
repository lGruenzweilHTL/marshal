---
icon: fa-solid fa-file-lines
description: "Leg ein Agent-Template an, um einzuschränken, was eine Aufgabe oder ein Subagent sehen und tun kann, und delegiere Arbeit daran."
---

# Agent-Templates

Leg ein Agent-Template an, um einzuschränken, was eine Aufgabe oder ein Subagent sehen und tun kann, und delegiere Arbeit daran.

## Bevor du anfängst

- Zugriff auf das Volume `/config`. Templates laden im laufenden Betrieb neu.
- Vollständige Referenz des Formats: [Template-Format](../configuration/templates-format.md).

## Schritte

::: steps
1. **Schreib das Template**

   `/config/templates/<name>.md`: Frontmatter plus ein Prompt als Inhalt. Der Dateiname ohne `.md` muss gleich `name` sein.

   ```markdown
   ---
   name: house-inspector
   description: Reads the current state of the house and reports what needs attention. Cannot change anything.
   modelTier: fast
   tools: ["ha.homeassistant__GetLiveContext", "agent.load_skill"]
   skills: [house-status]
   maxSteps: 4
   maxAdvisorCalls: 0
   ---

   You inspect the house on behalf of another agent. Load the house-status skill and follow it.
   Answer with facts only, in at most five short lines. Do not ask questions; nobody can answer them.
   ```

   | Schlüssel | Beschreibung |
   |---|---|
   | `name`, `description` | Pflicht. Die Beschreibung sagt Aufrufern von `agent.delegate`, wann sie das Template nutzen. |
   | `modelTier` | `fast` oder `standard`. Weggelassen: Sprach-Turns nutzen fast, andere standard. |
   | `tools` | Allowlist von Glob-Mustern für Werkzeug-IDs. Ein Durchlauf sieht nur passende Werkzeuge; `[]` heißt keine. |
   | `skills` | Skills, die das Template laden darf. Weggelassen: alle. |
   | `maxSteps` | 1 bis 50. Weggelassen: `agent.maxSteps`. |
   | `maxAdvisorCalls` | 0 bis 10. Weggelassen: `agent.advisor.maxCallsPerRun`. |
   | `minWakeupInterval` | Mindestens `00:01:00`. Kürzeste Weckverzögerung für Aufgaben mit diesem Template. |

   Unbekannte Schlüssel sind Fehler. Ein fehlerhaftes Template wird für sich allein abgewiesen: `<file>: modelTier '<x>' must be 'fast' or 'standard'.`, `maxSteps must be between 1 and 50.`, `name '<x>' must match the file name '<file>'.`

2. **Nutze es als Template einer Aufgabe**

   Setz `template: <name>` in `tasks.create` oder `tasks.update`. Der Durchlauf sieht nur die Werkzeuge des Templates, als Eigentümer der Aufgabe, unter der Richtlinie des Ursprungssatelliten. Das [tägliche Briefing](tasks/daily-briefing.md) ist so gebaut.

3. **Delegiere daran**

   Jeder Durchlauf kann `agent.delegate(template, task)` aufrufen. Der Subagent führt eine verschachtelte Schleife mit Prompt, Werkzeugen, Skills und Stufe des Templates aus, und sein letzter Text kommt als Ergebnis des Werkzeugs zurück. Die `task` muss vollständig sein: Der Subagent sieht sonst nichts.

   Die verschachtelte Schleife:

   - Nutzt denselben Guard, Eigentümer, dieselbe Satellitenrichtlinie und Belastung wie der Aufrufer. Liest der Subagent Mail, ist der Aufrufer danach belastet.
   - Führt nie Aktionen der Bestätigungsstufe aus. Werkzeuge der Bestätigungsstufe sind verborgen, und ein Aufruf, der zu confirm auflöst, wird abgelehnt mit `Not done: this action needs the user's confirmation, which a subagent cannot ask for. Report the needed action to your caller in your answer.`
   - Ist in der Tiefe durch `agent.maxDelegationDepth` begrenzt (Standard 3). Am Limit, oder wenn kein Template delegierbar ist, ist `agent.delegate` verborgen.

   Das eingebaute Template `default` (alle Werkzeuge, alle Skills, kein zusätzlicher Prompt) dient den Turns von Unterhaltungen; eine `default.md` ersetzt es. Es ist nicht delegierbar.
:::

## Prüfen, ob es funktioniert

Bitte Marshal, das Template zu nutzen („frag den House Inspector, wie es dem Haus geht“). Unter **Läufe** zeigt der Durchlauf den Schritt `agent_delegate` und die verschachtelten Schritte.

## Verwandte Seiten

::: cards
- [Skills](skills.md)
- [Tägliches Briefing](tasks/daily-briefing.md)
- [Risikoregeln](risk-rules.md)
- [Template-Format](../configuration/templates-format.md)
- [`agent`](../configuration/marshal-yaml/agent.md)
- [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md)
:::
