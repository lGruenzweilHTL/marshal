---
icon: fa-solid fa-server
description: "Einstellungen für die ganze Instanz: Standard-Zeitzone, Orte der Volumes, Log-Level und Trace-Export."
---

# `instance`, `paths`, `logging`, `telemetry`

Einstellungen für die ganze Instanz: Standard-Zeitzone, Orte der Volumes, Log-Level und Trace-Export.

## Beispiel

```yaml
instance:
  timeZone: Europe/Vienna

telemetry:
  otlpEndpoint: "http://otel-collector:4317"
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `instance.timeZone` | Zeichenkette (IANA-ID) | nicht gesetzt | Zeitzone, mit der neue Benutzer starten. Nicht gesetzt: die Zeitzone von Home Assistant, wenn `ha` konfiguriert ist, sonst UTC. |
| `paths.config` | Zeichenkette | `/config` | Konfigurationsverzeichnis. Setz es mit `MARSHAL__Paths__Config`: Marshal liest diese Variable, bevor `marshal.yaml` existiert. |
| `paths.data` | Zeichenkette | `/data` | Datenbank, Data-Protection-Schlüssel, Sicherungen, Artefakte, Snapshots, Notizen. |
| `paths.secrets` | Zeichenkette | `/run/secrets` | Verzeichnis der Docker-Secret-Dateien. |
| `logging.logLevel.default` | Zeichenkette | Standard von .NET | Normales .NET-Log-Level (`Trace`, `Debug`, `Information`, ...). `Debug` und `Trace` geben bei Konfigurationsfehlern auch Stacktraces aus. Der Abschnitt `logging` ist die normale .NET-Konfiguration `Logging`. |
| `telemetry.otlpEndpoint` | URL | nicht gesetzt | OTLP-gRPC-Endpunkt für Traces und Metriken. Nicht gesetzt: kein Export. |
| `telemetry.captureContent` | Boolean | `false` | Fügt Spans die Benutzereingabe, Werkzeugargumente, Werkzeugergebnisse und die Antwort hinzu, geschwärzt wie bei der Aufzeichnung der Durchläufe. Nie System-Prompts oder vollständige Modellnachrichten. Solange es `true` ist, schreibt Marshal beim Start eine Warnung ins Log. Siehe [Inhalte mitschneiden](../../guides/observability.md#inhalte-mitschneiden-standardmäßig-aus). |

`paths.*` sind zum Setzen über die Variablen `MARSHAL__Paths__*` gedacht, nicht in `marshal.yaml`.

## Validierung

- `instance.timeZone is not an IANA time zone.`, wenn der Wert keine bekannte IANA-ID ist.
- Der Start scheitert, wenn `telemetry.captureContent` weder `true` noch `false` ist.

## Verwandte Seiten

::: cards
- [Übersicht](../index.md)
- [Observability](../../guides/observability.md)
- [Datenverzeichnis](../../reference/data-directory.md)
:::
