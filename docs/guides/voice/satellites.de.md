---
icon: fa-solid fa-tower-broadcast
description: "Gib einzelnen Assist-Satelliten über /config/satellites.yaml eine Antwortlänge, Notizen, ein Display, einen Musiklautsprecher und Grenzen für Werkzeuge."
---

# Satellitenprofile konfigurieren

Gib einzelnen Assist-Satelliten über `/config/satellites.yaml` eine Antwortlänge, Notizen, ein Display, einen Musiklautsprecher und Grenzen für Werkzeuge.

## Bevor du anfängst

- Marshal ist mit Home Assistant verbunden, und ein Satellit nutzt Marshal als Konversationsagenten ([Assist-Pipeline](assist-pipeline.md)).
- Du kennst die Entitäts-ID des Satelliten, zum Beispiel `assist_satellite.kitchen`.
- Für `display` oder `speaker` ist das Display oder der Lautsprecher schon konfiguriert. Siehe [Displays](../displays.md) und [Musik](../integrations/music.md).

Profile sind optional. Ein Satellit ohne Profil nutzt Standardwerte: normale Ausführlichkeit, keine verweigerten Werkzeuge, kein Display, kein Lautsprecher.

## Schritte

::: steps
1. **Füg ein Profil hinzu**

   Bearbeite `/config/satellites.yaml`. Die Schlüssel sind Entitäts-IDs von Satelliten.

   ```yaml
   satellites:
     assist_satellite.kitchen:
       verbosity: short
       notes: "Cooking area. Timer and recipe questions are common."
       display: kitchen_tv          # ein Name aus displays.surfaces in marshal.yaml
       speaker: kitchen             # ein Name aus music.speakers in marshal.yaml
     assist_satellite.kids_room:
       verbosity: short
       denyConfirm: [printer.*, calendar.*]
       denyTools: [mail.*]
   ```

   | Schlüssel | Wirkung |
   |---|---|
   | `verbosity` | `short`, `normal` (Standard) oder `detailed`. Marshal gibt sie dem Modell als bevorzugte Antwortlänge mit. |
   | `notes` | Freier Text, der im System-Prompt zum Satellitenkontext hinzukommt. |
   | `denyTools` | Glob-Muster für Werkzeug-IDs. Passende Werkzeuge sind vor dem Modell verborgen und werden verweigert. |
   | `denyConfirm` | Glob-Muster für Werkzeug-IDs. Passende Werkzeuge der Bestätigungsstufe sind verborgen. |
   | `display` | Display im Raum des Satelliten. `artifacts_show` spielt dort ab. |
   | `speaker` | Musiklautsprecher im Raum des Satelliten. Die Musikwerkzeuge nutzen ihn standardmäßig. |

2. **Speichere die Datei**

   Marshal lädt die Datei im laufenden Betrieb neu. Ein Neustart ist nicht nötig.
:::

## Was die Richtlinie bewirkt

- **Verweigerte Werkzeuge** werden aus der Werkzeugliste gefiltert, das Modell sieht sie also nicht.
- **Verweigerte Bestätigung** verbirgt Werkzeuge der Bestätigungsstufe. Bei Werkzeugen, deren Stufe von den Argumenten abhängt (zum Beispiel ein Home-Assistant-Aufruf, der zu einem Schloss auflöst), bleibt das Werkzeug sichtbar, und der Guard verweigert einen Aufruf, der eine Bestätigung bräuchte, mit dem Fehler „not allowed from this device“. Es entsteht keine ausstehende Aktion.
- Ein Turn, den Marshal in den Hintergrund verschiebt, behält `denyTools` und `denyConfirm` des Satelliten.
- Marshal nennt dem Modell Bereich, Etage, Ortszeit und Sprache jedes Turns. Gibt es Musiklautsprecher, nennt es auch den Lautsprecher dieses Raums.

> [!NOTE]
> Profile schränken ein, was ein festes Gerät tun kann. Sie sind keine Grenze für Berechtigungen: Wer mit dem Satelliten spricht, ist für Marshal der Eigentümer.

## Prüfen, ob es funktioniert

1. Öffne **Einstellungen > Konfiguration** in der Weboberfläche (nur Admins). Der Satellit erscheint mit seiner Ausführlichkeit, seinen Notizen und Verweigerungslisten, und mit dem letzten Fehler beim Neuladen pro Datei.
2. Bitte den Satelliten um ein verweigertes Werkzeug. Marshal sagt, dass es das nicht kann.

## Fehlerbehebung

- Ein `display` oder `speaker`, der nichts Konfiguriertes nennt, ist ein Konfigurationsfehler der Datei, gemeldet als `satellites.<id>.display names no configured display '<name>' (displays.surfaces)`. Die vorherigen Profile bleiben aktiv, bis du die Datei korrigierst; die Konfigurationsansicht zeigt den Fehler.
- `verbosity` muss `short`, `normal` oder `detailed` sein.

## Verwandte Seiten

::: cards
- [Referenz von `satellites.yaml`](../../configuration/satellites-yaml.md)
- [Sichtbarkeit von Werkzeugen](../../concepts/tool-visibility.md)
- [Risikostufen](../../reference/risk-tiers.md)
:::
