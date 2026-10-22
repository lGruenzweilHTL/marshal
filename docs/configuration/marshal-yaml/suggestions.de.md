---
icon: fa-solid fa-lightbulb
description: "Schwellen der Annahmeschätzung, der Ablauf von Vorschlägen und die Einstellungen jedes Vorschlagsdetektors."
---

# `suggestions`

Schwellen der Annahmeschätzung, der Ablauf von Vorschlägen und die Einstellungen jedes Vorschlagsdetektors. Vorschläge laufen nur für Eigentümer mit eingeschaltetem „Marshal beim Lernen helfen“.

## Beispiel

```yaml
suggestions:
  explore: 3
  minAcceptance: 0.3
  pushAcceptance: 0.6
  expiry: "06:00:00"
  detectors:
    window_rain:
      minProbability: 70
      weatherEntity: weather.home
    energy_anomaly:
      factor: 2.0
    print_done_away:
      cooldown: "06:00:00"
    maintenance_counter:
      enabled: false
```

## Schlüssel

| Schlüssel | Typ | Standard | Beschreibung |
|---|---|---|---|
| `suggestions.explore` | Ganzzahl | `3` | Die ersten so vielen Kandidaten eines Detektors gehen in den Eingang, egal wie die Schätzung ausfällt. |
| `suggestions.minAcceptance` | Zahl | `0.3` | Unter dieser Schätzung wird ein Kandidat verworfen. |
| `suggestions.pushAcceptance` | Zahl | `0.6` | Ab dieser Schätzung wird ein Kandidat mit Priorität `normal` gepusht. Zwischen den beiden Schwellen geht er mit Priorität `low` in den Eingang. |
| `suggestions.expiry` | Dauer | `06:00:00` | Ein unbeantworteter Vorschlag läuft danach ab, oder früher, wenn seine Bedingung nicht mehr gilt. Abgelaufene Vorschläge ändern die Zähler nicht. |
| `suggestions.detectors.<id>.enabled` | Boolean | `true` | `false` schaltet den Detektor für jeden Eigentümer aus. |
| `suggestions.detectors.<id>.cooldown` | Dauer | `12:00:00` | Ein Kandidat zum selben Gegenstand (dasselbe Fenster, Gerät oder derselbe Drucker), der so kürzlich gesendet wurde, wird verworfen. |
| `suggestions.detectors.window_rain.minProbability` | Ganzzahl | `60` | Niederschlagswahrscheinlichkeit in Prozent, die als Regen zählt. |
| `suggestions.detectors.window_rain.weatherEntity` | Zeichenkette | erste `weather.*`-Entität | Die Wetter-Entität, deren Vorhersage gelesen wird. |
| `suggestions.detectors.energy_anomaly.factor` | Zahl | `1.5` | Ein Gerät ist auffällig, wenn sein Verbrauch gestern mindestens so viel mal sein Median der 14 Tage davor war. |

Die Schätzung ist (angenommen + 1) / (angenommen + abgelehnt + 2) pro Eigentümer und Detektor. Eigentümer schalten Detektoren stumm und setzen Zähler in **Präferenzen** zurück, nicht hier.

### Detektoren

| ID | Braucht | Schlägt vor |
|---|---|---|
| `window_rain` | `ha`, eine Wetter-Entität, `binary_sensor`-Entitäten mit der Geräteklasse `window` oder `door` | Ein offenes Fenster zu schließen, wenn innerhalb der Stunde Regen wahrscheinlich ist (stündliche Vorhersage; sonst die Tagesvorhersage für heute). |
| `energy_anomaly` | `ha` mit Geräten im Energie-Dashboard | Ein Gerät zu prüfen, das gestern ungewöhnlich viel Energie verbraucht hat. Geräte mit weniger als 14 Tagen Verlauf werden übersprungen. |
| `print_done_away` | `ha`, `printers`, die `person`-Entität des Eigentümers | Einen Drucker auszuschalten, der fertig wurde, während du weg bist. Bietet die Aktion nur mit `printers[].powerEntity` an. |
| `maintenance_counter` | `printers` oder `ha`-Sensoren mit der Zustandsklasse `total_increasing`, die Laufzeit oder Zyklen zählen | Einen [Wartungseintrag](maintenance.md) für einen Zähler, der keinen hat, mit einem YAML-Schnipsel. |

## Validierung

- `suggestions.explore cannot be negative.`
- `suggestions.minAcceptance and suggestions.pushAcceptance must be between 0 and 1, minAcceptance not above pushAcceptance.`
- `suggestions.expiry must be positive.`
- `suggestions.detectors.<id>: a detector id must match [a-z][a-z0-9_]*.`
- `suggestions.detectors.<id>.cooldown cannot be negative.`
- `suggestions.detectors.<id>.minProbability must be between 0 and 100.`
- `suggestions.detectors.<id>.factor must be greater than 1.`

## Verwandte Seiten

::: cards
- [Vorschläge bekommen](../../guides/suggestions.md)
- [`maintenance`](maintenance.md)
- [`notifications`](notifications.md)
:::
