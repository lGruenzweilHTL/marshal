---
icon: fa-solid fa-circle-check
description: "Einstellungen zum Bestätigen einer Aktion der Bestätigungsstufe innerhalb einer Unterhaltung."
---

# `confirmation`

Einstellungen zum Bestätigen einer Aktion der Bestätigungsstufe innerhalb einer Unterhaltung. Änderungen brauchen einen Neustart.

## Beispiel

```yaml
confirmation:
  ttl: "00:01:00"
  judgeTimeout: "00:00:03"
  keywords:
    en: [confirm, confirmed, approved, "yes do it"]
    de: [bestätigt, bestätigen, "ja mach", "ja mach das"]
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `confirmation.ttl` | Dauer | `00:01:00` | Wie lange eine ausstehende Aktion bestätigt werden kann. Bereich `00:00:05` bis `01:00:00`. |
| `confirmation.judgeTimeout` | Dauer | `00:00:03` | Zeitüberschreitung des Bestätigungsprüfers. Läuft sie ab, wird die Aktion verworfen. Bereich `00:00:00.5` bis `00:00:30`. |
| `confirmation.keywords.<language>` | Liste von Zeichenketten | Listen `en` und `de` unten | Bestätigungsphrasen mit genauem Treffer pro Sprachcode. Eine konfigurierte Sprache ersetzt ihre eingebaute Liste. |

Eingebaute Schlüsselwörter: `en`: `confirm`, `confirmed`, `approved`, `yes do it`. `de`: `bestätigt`, `bestätigen`, `ja mach`, `ja mach das`.

Der Abgleich normalisiert Äußerung und Schlüsselwörter (trimmen, Kleinschreibung, Satzzeichen entfernen, Leerzeichen zusammenfassen) und vergleicht den ganzen Text. Die Sprache sind die ersten zwei Buchstaben der Sprache der Anfrage. Andere Sprachen haben keine Schlüsselwörter; dort bestätigt nur der Prüfer.

## Validierung

- `confirmation.ttl must be between 00:00:05 and 01:00:00.`
- `confirmation.judgeTimeout must be between 00:00:00.5 and 00:00:30.`

## Verwandte Seiten

::: cards
- [Risiko und Bestätigung](../../concepts/risk-and-confirmation.md)
- [Risikostufen](../../reference/risk-tiers.md)
- [`tasks`](tasks.md)
  `tasks.approvalTtl` für Freigaben im Hintergrund
:::
