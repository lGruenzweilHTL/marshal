---
icon: fa-solid fa-hammer
description: "Regeln, um einer bestehenden Werkzeugquelle ein Werkzeug für das Modell hinzuzufügen."
---

# Ein Werkzeug hinzufügen

Regeln, um einer bestehenden Werkzeugquelle ein Werkzeug für das Modell hinzuzufügen. Das Design der Änderung nennt die Risikostufe des Werkzeugs und wann es verborgen ist ([OpenSpec-Ablauf](openspec-workflow.md)).

::: prompt Für einen Coding-Agenten
Read CLAUDE.md, STYLE_GUIDE.md and
https://lgruenzweilhtl.github.io/marshal/development/adding-a-tool.md.
Then implement the tool of the OpenSpec change openspec/changes/<change>/
test first: write the failing tests, add the descriptor with the tier the
design names, visibility and validation, and the rows in
docs/reference/tools.md. Never call a real model in tests. Run the tests of
the changed projects and `npx clay-doc@5 check` until both pass.
:::

## Der Vertrag

Ein Werkzeug implementiert `ITool` (`src/Marshal.Abstractions/Tools/ITool.cs`) und lebt in einer `IToolSource`; die Registry führt alle Quellen zusammen.

| Member | Zweck |
|---|---|
| `Descriptor` | Id, Beschreibung, JSON-Schema der Parameter, Standardstufe, Flags. |
| `IsAvailableAsync(context, ct)` | Sichtbarkeit abhängig vom Kontext (Sprach-Turns, Satellit, Template). Vor jedem Modellaufruf geprüft und erneut vom Guard. |
| `Validate(arguments)` | Prüfungen, die vor jeder Stufe gelten, etwa eine Allowlist. Ein Fehler verweigert den Aufruf ohne Bestätigung. |
| `Summarize(arguments, language)` | Kurzer Text für Bestätigungsfragen. |
| `InvokeAsync(invocation, ct)` | Führt den Aufruf aus, gibt `Result<ToolOutput>` zurück. |

```csharp
private static ToolDescriptor Descriptor(string name, string description, string schema) => new()
{
    Id = new ToolId("infra", name),
    Description = description,
    ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
    DefaultTier = RiskTier.Safe,
};
```

## Regeln

- **Verbotene Fähigkeiten bekommen keinen Code.** Kein Senden von Mail, kein roher G-Code und keine freien Gerätebefehle, kein Werkzeug, das eine vom Aufrufer gewählte URL abruft. Schreib nie ein Werkzeug, um es dann zu verbieten.
- **Ids.** Interne Id `<namespace>.<snake_case_name>`; das Modell sieht `<namespace>_<name>`. Risikoregeln, Satellitenrichtlinien und Allowlists von Templates nutzen die interne Id. Der Namespace benennt die Fähigkeit, nicht den Anbieter.
- **Schema.** Streng. Nimm Ids bekannter Dinge, nie freie Befehle oder URLs. Schreib die Beschreibung für das Modell: was das Werkzeug zurückgibt und wann es passt.
- **Stufe.** Setz die Standardstufe im Descriptor. Hängt die Stufe vom Ziel ab, ergänze argumentabhängige Regeln in `deploy/config.example/risk.yaml`. Mehrere Ziele: Die höchste Stufe gewinnt. Ein nicht auflösbares Ziel fällt auf `confirm`.
- **Sichtbarkeit ist keine Durchsetzung.** Der Guard führt `IsAvailableAsync` und `Validate` erneut aus.
- **Fehler.** Erwartete Fehler geben ein gescheitertes `Result` mit stabilem Code zurück (`infra.invalid_arguments`). Wirf nur bei Bugs.
- **Nicht vertrauenswürdige Ausgabe.** Ausgabe mit Inhalten aus Mail, Feeds, Notizen oder Webseiten wird als nicht vertrauenswürdig markiert; der Durchlauf ist dann belastet, und seine Gedächtnis- und Aufgabenänderungen steigen auf `confirm`.
- **Tests** decken den Normalfall, ungültige Argumente, die Stufe, die Sichtbarkeit und den Eigentümer ab. Nutz `FakeTool` / `FakeToolSource` für Tests von Registry und Guard, den Fake des Clients der Integration für das Werkzeug selbst.

## In derselben Änderung aktualisieren

- Zeilen in der [Werkzeugreferenz](../reference/tools.md), mit Stufe und Sichtbarkeit.
- Die Anleitung der Integration, und `deploy/config.example/risk.yaml` plus die [Referenz der Risikostufen](../reference/risk-tiers.md), wenn du Regeln ergänzt hast.

## Prüfen, ob es funktioniert

- Das Werkzeug steht in der Liste für einen Kontext, der es nutzen darf, und nicht für einen, der es nicht darf.
- Ein direkter Aufruf an der Liste vorbei wird trotzdem vom Guard abgelehnt.
- Ein Aufruf der Bestätigungsstufe wird eingefroren gespeichert und führt genau die gespeicherten Argumente aus.

## Verwandte Seiten

::: cards
- [Eine Integration hinzufügen](adding-an-integration.md)
- [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md)
- [Risikostufen](../reference/risk-tiers.md)
:::
