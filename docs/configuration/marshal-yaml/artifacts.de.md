---
icon: fa-solid fa-paperclip
description: "Grenzen des Artefaktspeichers in /data/artifacts: Uploads, Ausgaben des Executors, behaltene Snapshots und gespeicherte Anhänge."
---

# `artifacts`

Grenzen des Artefaktspeichers in `/data/artifacts`: Uploads, Ausgaben des Executors, behaltene Snapshots und gespeicherte Anhänge.

## Beispiel

```yaml
artifacts:
  quotaMb: 5120
  maxUploadMb: 200
  signedLinkMinutes: 15
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `artifacts.quotaMb` | Ganzzahl (MB) | `5120` | Speicher pro Benutzer. Ein Schreibvorgang, der ihn überschreiten würde, scheitert mit einem Kontingentfehler und speichert nichts. |
| `artifacts.maxUploadMb` | Ganzzahl (MB) | `200` | Größte einzelne Datei für Uploads, Ausgaben des Executors und Anhänge. Ein größerer Web-Upload bekommt 413. |
| `artifacts.signedLinkMinutes` | Ganzzahl (Minuten) | `15` | Lebensdauer signierter Inhaltslinks, zum Beispiel für eine Benachrichtigung aufs Handy. |

Gerenderte 3D-Seiten für Displays werden in `/data/cache/renditions` gecacht, höchstens 512 MB. Dieser Cache ist verzichtbar und nicht konfigurierbar.

## Validierung

- `artifacts.quotaMb must be at least 1.`
- `artifacts.maxUploadMb must be at least 1.`
- `artifacts.signedLinkMinutes must be between 1 and 1440.`

## Verwandte Seiten

::: cards
- [Anleitung zu Artefakten](../../guides/artifacts.md)
- [`displays`](displays.md)
:::
