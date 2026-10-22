---
icon: fa-solid fa-film
description: "Schwärzung der Aufzeichnungen von Durchläufen: welche Werkzeugergebnisse vollständig gespeichert werden."
---

# `recording`

Schwärzung der Aufzeichnungen von Durchläufen: welche Werkzeugergebnisse vollständig gespeichert werden.

## Beispiel

```yaml
recording:
  redactTools: ["mail.*"]
  keepContentTools: []
  maxContentLength: 8000
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `recording.redactTools` | Liste von Globs auf Werkzeug-IDs | `[]` | Ergebnisse passender Werkzeuge werden nur als Verweise gespeichert, zusätzlich zu als sensibel markierten Werkzeugen (bei denen das immer so ist). Das Reasoning späterer Modellaufrufe im selben Durchlauf wird nicht gespeichert, und die Werkzeugschritte im Chat zeigen für diese Werkzeuge keine Zusammenfassung des Ergebnisses. |
| `recording.keepContentTools` | Liste von Globs auf Werkzeug-IDs | `[]` | Ergebnisse passender Werkzeuge werden vollständig gespeichert, auch wenn das Werkzeug als sensibel markiert ist. |
| `recording.maxContentLength` | Ganzzahl | `8000` | Längere Inhalte und längeres Reasoning werden in den Nutzdaten der Schritte abgeschnitten. |

Globs nutzen `*` gegen die Werkzeug-ID (zum Beispiel `mail.*`). Logs enthalten unabhängig von diesen Einstellungen nie Secrets, Nachrichtentexte, Notizinhalte, Reasoning oder vollständige Prompts. Der Abschnitt hat keinen Validator.

## Verwandte Seiten

::: cards
- [`maintenance`](maintenance.md)
  Aufbewahrung der Nutzdaten
- [Observability](../../guides/observability.md)
- [Datenschutz](../../concepts/privacy.md)
:::
