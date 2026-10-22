---
icon: fa-solid fa-shield-halved
description: "/config/risk.yaml enthält die geordneten Regeln, die die Risikostufe von Werkzeugaufrufen festlegen."
---

# `risk.yaml`

`/config/risk.yaml` enthält die geordneten Regeln, die die [Risikostufe](../reference/risk-tiers.md) von Werkzeugaufrufen festlegen. Die erste passende Regel gewinnt. Marshal lädt die Datei ohne Neustart neu.

## Beispiel

```yaml
risk:
  - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
    tier: confirm
  - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
    tier: confirm
  - match: { tool: "tasks.create", args: { template: briefing } }
    tier: confirm
  - match: { tool: "ha.*" }
    tier: safe
```

## Schlüssel

Die Datei hat einen Schlüssel auf oberster Ebene. Eine fehlende oder leere Datei bedeutet keine Regeln.

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `risk` | Liste von Regeln | `[]` | Regeln, der Reihe nach ausgewertet. |
| `risk[].match` | Abbildung | Pflicht | Bedingungen. Muss `tool` enthalten. |
| `risk[].match.tool` | Zeichenkette (Glob) | Pflicht | Glob auf die interne Werkzeug-ID (`namespace.name`, zum Beispiel `ha.intent__HassTurnOn`). `*` trifft eine beliebige Zeichenfolge, `?` ein Zeichen. Beachtet Groß-/Kleinschreibung. |
| `risk[].match.args` | Abbildung | keiner | Bedingungen an Argumente. Jeder Schlüssel ist ein Argumentname der obersten Ebene; der Wert ist ein Wert oder eine Liste von Werten. Der Aufruf trifft, wenn sein Argument einem davon gleicht (ohne Beachtung der Groß-/Kleinschreibung). Zeichenketten, Zahlen und Booleans werden als Text verglichen. Ein fehlendes Argument trifft nie. |
| `risk[].match.<attribute>` | Zeichenkette oder Liste von Zeichenketten | keiner | Jeder andere Schlüssel ist eine Zielbedingung, zum Beispiel `domain`, `deviceClass`, `area`. Er wird mit jedem konkreten Ziel verglichen, zu dem der Aufruf aufgelöst wird (ohne Beachtung der Groß-/Kleinschreibung, jeder gelistete Wert trifft). |
| `risk[].tier` | `safe`, `confirm` oder `forbidden` | Pflicht | Stufe, wenn die Regel trifft. Ohne Beachtung der Groß-/Kleinschreibung. Zahlen werden abgelehnt. |

Alle Bedingungen einer Regel müssen gelten. Eine Regel mit nur `tool` trifft jeden Aufruf dieser Werkzeuge.

## Zielbedingungen

Home-Assistant-Werkzeuge (`ha.*`) lösen ihre Ziele über die Registry auf. Jeder Filter im Aufruf (Name, Bereich, Stockwerk, Domain, Geräteklasse) wird zu den konkreten Entitäten erweitert. Jedes Ziel trägt diese Attribute:

| Attribut | Wert |
|---|---|
| `domain` | Domain der Entität, zum Beispiel `lock`, `cover`, `light`. |
| `deviceClass` | Die Geräteklasse der Entität, wenn sie eine hat. |
| `area` | Der Bereichsname der Entität, wenn sie einen hat. |

Regeln werden pro Ziel ausgewertet, und die höchste Stufe gewinnt. Ein Aufruf, dessen Ziele sich nicht auflösen lassen, zu nichts aufgelöst werden oder zu einem Namensraum ohne Resolver gehören, während eine Zielregel seine Werkzeug-ID trifft, ist `confirm` mit dem Grund `unresolved_target`.

## Werkzeuge, die keine Regel trifft

Ein Aufruf, den keine Regel trifft, nutzt die Standardstufe des Werkzeugs. Eingebaute Werkzeuge nutzen den Standard, den ihr Deskriptor deklariert. Externe Werkzeuge (MCP-Server) stehen standardmäßig auf `confirm`, füge also eine ausdrückliche Regel wie `{ tool: "ha.*" } -> safe` hinzu, damit sie ohne Bestätigung laufen. Für eingebaute und externe Werkzeuge gelten dieselben Regeln.

## Validierung

Der Start scheitert, oder ein Neuladen behält die vorherigen Regeln und schreibt den Fehler ins Log, wenn:

- das YAML ungültig ist oder einen unbekannten Schlüssel auf oberster Ebene hat: `risk.yaml: invalid value at line <n>: ...`
- `risk.yaml: risk[<i>].match.tool is required and must be a glob string.`
- `risk.yaml: risk[<i>].tier must be safe, confirm or forbidden.`
- `risk.yaml: risk[<i>].match.args must map argument names to values.`
- `risk.yaml: risk[<i>].match.args.<name> must be a value or a list of values.`
- `risk.yaml: risk[<i>].match.<key> must be a value or a list of values.` (eine Liste darf nicht leer sein und nur Zeichenketten enthalten)
- ein Wert wie ein Zugangsdatum aussieht (siehe [Secrets-Referenz](secrets-reference.md#prüfung-auf-wörtliche-secrets)).

## Verwandte Seiten

::: cards
- [Risikostufen](../reference/risk-tiers.md)
- [Anleitung zu Risikoregeln](../guides/risk-rules.md)
- [Risiko und Bestätigung](../concepts/risk-and-confirmation.md)
- [`satellites.yaml`](satellites-yaml.md)
:::
