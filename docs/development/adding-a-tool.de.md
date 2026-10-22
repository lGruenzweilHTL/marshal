---
icon: fa-solid fa-hammer
description: "Einer bestehenden Werkzeugquelle ein Werkzeug für das Modell hinzufügen."
---

# Ein Werkzeug hinzufügen

Einer bestehenden Werkzeugquelle ein Werkzeug für das Modell hinzufügen.

## Bevor du anfängst

- Die Änderung ist freigegeben, und das Design nennt die Risikostufe des Werkzeugs und wann es verborgen ist ([OpenSpec-Ablauf](openspec-workflow.md)).
- Die Fähigkeit ist erlaubt. Marshal hat keinen Pfad zum Senden von Mail, keinen rohen G-Code und kein Werkzeug, das eine vom Aufrufer gewählte URL abruft. Schreib kein Werkzeug, um es dann zu verbieten.

Ein Coding-Agent kann die Arbeit aus der freigegebenen Änderung erledigen; setz den Namen deiner Änderung für `<change>` ein:

::: prompt Für einen Coding-Agenten
Read CLAUDE.md, STYLE_GUIDE.md and
https://lgruenzweilhtl.github.io/marshal/development/adding-a-tool.md.
Then implement the tool of the OpenSpec change openspec/changes/<change>/
test first: write the failing tests, add the descriptor with the tier the
design names, visibility and validation, and the rows in
docs/reference/tools.md. Never call a real model in tests. Run the tests of
the changed projects and `npx clay-doc@5 check` until both pass.
:::

## Wie Werkzeuge aufgebaut sind

Ein Werkzeug implementiert `ITool` (`src/Marshal.Abstractions/Tools/ITool.cs`). Eine Gruppe von Werkzeugen lebt in einer `IToolSource`. Die Quelle gibt ihre Werkzeuge aus `GetToolsAsync` zurück; das Werkzeugregister führt alle Quellen zusammen.

| Member | Zweck |
|---|---|
| `Descriptor` | ID, Beschreibung, JSON-Schema der Parameter, Standardstufe, Markierungen. |
| `IsAvailableAsync(context, ct)` | Sichtbarkeit des einzelnen Werkzeugs. Vor jedem Modellaufruf geprüft und vom Guard noch einmal. Standard ist `true`. |
| `Validate(arguments)` | Prüfungen der Argumente, die gelten müssen, bevor eine Stufe greift, etwa eine Allowlist. Ein Fehlschlag lehnt den Aufruf ohne Bestätigung ab. |
| `Summarize(arguments, language)` | Kurzer Text für Bestätigungsfragen. |
| `InvokeAsync(invocation, ct)` | Führt den Aufruf aus und gibt `Result<ToolOutput>` zurück. |

## Schritte

::: steps
1. **Die fehlschlagenden Tests schreiben**

   Deck den Normalfall ab, ungültige Argumente, die Stufe, die Sichtbarkeit und den Eigentümer. Nutze `FakeTool` und `FakeToolSource` aus `tests/Marshal.Testing`, wenn der Test das Register oder den Guard betrifft, und den Fake des Clients deiner Integration, wenn er das Werkzeug betrifft. Ruf nie ein echtes Modell auf.

2. **Den Deskriptor festlegen**

   ```csharp
   private static ToolDescriptor Descriptor(string name, string description, string schema) => new()
   {
       Id = new ToolId("infra", name),
       Description = description,
       ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
       DefaultTier = RiskTier.Safe,
   };
   ```

   Regeln:

   - Die interne ID ist `<namespace>.<snake_case_name>`. Das Modell sieht `<namespace>_<name>`.
   - Risikoregeln, Richtlinien der Satelliten und Allowlists der Templates verweisen auf die interne ID.
   - Schreib die Beschreibung für das Modell. Sag, was das Werkzeug zurückgibt und wann es zu nutzen ist.
   - Halte das Schema streng. Nimm IDs bekannter Elemente, nie freie Befehle oder URLs.

3. **Die Stufe wählen**

   `safe` läuft sofort. `confirm` wird auf dem Server eingefroren und braucht eine Benutzeraktion. `forbidden` wird aus der Werkzeugliste entfernt. Wähle die Standardstufe im Deskriptor und ergänze Regeln mit Argumentbezug in `risk.yaml`, wenn die Stufe vom Ziel abhängt (zum Beispiel `ha.*` gegen Schlösser). Wird ein Aufruf zu mehreren Zielen aufgelöst, gewinnt die höchste Stufe. Ein nicht auflösbares Ziel fällt auf confirm.

4. **Sichtbarkeit und Validierung umsetzen**

   Überschreib `IsAvailableAsync`, wenn das Werkzeug vom Kontext abhängt, etwa von Sprach-Turns, dem Satelliten oder dem Template. Nutze `Validate` für Allowlists. Der Guard führt beides erneut aus; verlass dich nie allein auf die Sichtbarkeit.

5. **Den Aufruf umsetzen**

   Gib bei erwarteten Fehlern ein gescheitertes `Result` mit einem stabilen Fehlercode (`infra.invalid_arguments`) zurück. Der Executor macht daraus eine strukturierte Nachricht für das Modell. Wirf nur bei Bugs.

   Behandle Text von außen als nicht vertrauenswürdig. Ausgaben, die Inhalte aus Mail, Feeds, Notizen oder dem Web tragen, müssen als untrusted markiert sein, damit der Durchlauf belastet wird. In einem belasteten Durchlauf steigen Schreibvorgänge in Gedächtnis und Aufgaben auf die Bestätigungsstufe.

6. **Registrieren und dokumentieren**

   Füg das Werkzeug der Werkzeugliste der Quelle hinzu. Dann:

   - Ergänze Zeilen in der [Werkzeug-Referenz](../reference/tools.md).
   - Aktualisiere die Anleitung der Integration und `deploy/config.example/risk.yaml`, wenn du Regeln hinzugefügt hast.
:::

## Prüfen, ob es funktioniert

- Das Werkzeug erscheint in der Liste für einen Kontext, der es nutzen darf, und nicht für einen, der es nicht darf.
- Ein direkter Aufruf, der die Liste umgeht, wird vom Guard trotzdem abgelehnt.
- Ein Aufruf der Bestätigungsstufe wird eingefroren gespeichert und führt genau die gespeicherten Argumente aus.

## Verwandte Seiten

::: cards
- [Eine Integration hinzufügen](adding-an-integration.md)
- [Werkzeug-Sichtbarkeit](../concepts/tool-visibility.md)
- [Risikostufen](../reference/risk-tiers.md)
:::
