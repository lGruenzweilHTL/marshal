---
icon: fa-solid fa-flask
description: "Betreibe Marshal nur mit einem konfigurierten Modell und sieh trotzdem jede Ansicht und die wichtigsten Werkzeugabläufe, mit simulierten Integrationen und Beispielverlauf."
---

# Marshal im Demo-Modus ausprobieren

Betreibe Marshal nur mit einem konfigurierten Modell und sieh trotzdem jede Ansicht und die wichtigsten Werkzeugabläufe, mit simulierten Integrationen und Beispielverlauf.

## Bevor du anfängst

- Eine laufende Marshal-Instanz mit einem funktionierenden Abschnitt `models`. Der Demo-Modus ersetzt das Modell nicht; ohne Modell schlägt der Start fehl, genau wie ohne Demo-Modus.
- Ein Embedding-Modell, wenn du das Beispielgedächtnis willst. Ohne erreichbares Embedding-Modell fällt der Gedächtnisteil der Beispieldaten weg.

## Schritte

::: steps
1. **Schalte den Demo-Modus ein**

   Füge `/config/marshal.yaml` den Abschnitt `demo` hinzu:

   ```yaml [/config/marshal.yaml]
   demo:
     enabled: true
   ```

2. **Starte Marshal neu**

   ```sh
   docker compose restart marshal
   ```

   Das Log warnt, dass der Demo-Modus an ist. Beim ersten Start mit Demo-Modus legt Marshal die Beispieldaten für den Eigentümer der Instanz an. Gibt es den Eigentümer noch nicht, entstehen die Daten direkt nachdem die Ersteinrichtung ihn anlegt.

3. **Erkunde**

   Jede dieser Integrationen ist simuliert, solange sie nicht konfiguriert ist. Eine konfigurierte Integration bleibt echt.

   | Integration | Was die Simulation enthält |
   |---|---|
   | Home Assistant | Bereiche, Lichter, Klima, Sensoren (darunter ein offenes Werkstattfenster), eine Wettervorhersage mit Regen in den nächsten Stunden, Zonen, Energie, Timer, Musikplayer und einen Kalender. Dienstaufrufe ändern die simulierten Zustände. Ohne MCP-Server `ha` arbeiten die Werkzeuge zur Hausteuerung `ha.intent__HassTurnOn`, `ha.intent__HassTurnOff`, `ha.intent__HassLightSet`, `ha.intent__HassClimateSetTemperature` und `ha.homeassistant__GetLiveContext` auf denselben Zuständen. |
   | CalDAV-Kalender | Einen Arbeitskalender `caldav:demo/work` mit einem wöchentlichen Team-Standup (Organisator und Teilnehmende), einem abgelehnten Review und einer Einladung zu einem Projekt-Kickoff, die zwei Minuten nach dem Start als `calendar.event_created` und `calendar.invitation` eintrifft. Einzelne Standups lassen sich verschieben. |
   | Drucker | Einen Drucker, `voron`, mitten in einem Druck, der mit der Zeit fortschreitet, mit Snapshots, einem Webcam-Bild und Auftragssummen (Druckstunden und Filament), die mit der Zeit wachsen. Ohne konfigurierte `maintenance.items` ist seine Düse das Wartungsobjekt `voron_nozzle`, alle 300 Druckstunden. |
   | Mail | Ein Mailkonto `demo` mit Beispielnachrichten. Entwürfe bleiben bis zum nächsten Neustart. Es gibt keinen Sendeweg. |
   | FreshRSS | Feeds in zwei Kategorien, mit Artikeln und deren Volltext. |
   | Notizen | Eine kleine Sammlung von Markdown-Notizen, indiziert wie ein echtes Notizen-Repository. |
   | Websuche | Feste Suchergebnisse für jede Anfrage. Ergebnisseiten werden nie geöffnet, und keine Suchmaschine wird kontaktiert. |

   Die simulierten Werkzeuge haben dieselben IDs, Risikostufen und Guard-Regeln wie die echten. Das Abbrechen des Drucks braucht zum Beispiel weiterhin deine Bestätigung.

   Der Beispielverlauf gehört dem Eigentümer der Instanz und ist relativ zum Zeitpunkt des Anlegens datiert:

   - Unterhaltungen im Web und per Stimme von den Satelliten `kitchen` und `office`, darunter ein Turn, der in den Hintergrund gewechselt ist, mit seinem Ergebnis
   - Durchläufe mit ihren Schritten
   - eine Aufgabe `morning` mit früheren Ergebnissen und eine Aufgabe, die auf deine Freigabe zum Abbrechen des Drucks wartet
   - Benachrichtigungen im Eingang
   - Gedächtniseinträge mit Kanten, ein geschlossener Eintrag, ein Eintrag mit früherer Version und Gültigkeitszeit und ein offener Duplikatvorschlag in der Prüfung
   - mit dem simulierten Drucker das Wartungsobjekt `voron_nozzle` bei 286 von 300 Druckstunden, sodass Start es als bald fällig zeigt

   Simulierter Zustand, etwa ein eingeschaltetes Licht oder ein Entwurf, beginnt bei jedem Neustart von vorn.

   Um [Vorschläge](suggestions.md) zu sehen, schalte unter **Präferenzen** die Option **Marshal beim Lernen helfen** ein. Das offene Werkstattfenster und die Regenvorhersage ergeben dann einen Vorschlag `window_rain`.
:::

## Das Abzeichen für Screenshots ausblenden

Die Navigation zeigt ein Abzeichen „Demo“, solange der Demo-Modus an ist. Blende es so aus:

```yaml
demo:
  enabled: true
  showBadge: false
```

Die Systemansicht markiert weiterhin jede simulierte Integration.

## Die Beispieldaten entfernen

```sh
docker compose exec marshal marshal demo reset
```

Das löscht genau die angelegten Zeilen. Aufgaben, Unterhaltungen und Gedächtnis, die du oder der Agent während der Demo angelegt habt, bleiben. Nach einem Zurücksetzen legt ein Neustart nichts neu an. Um die Daten neu anzulegen, führe aus:

```sh
docker compose exec marshal marshal demo seed
```

## Den Demo-Modus ausschalten

Setz `demo.enabled: false` oder entferne den Abschnitt und starte neu. Die Beispieldaten bleiben, bis du `marshal demo reset` ausführst.

> [!WARNING]
> Der Demo-Modus füllt auf einer echten Instanz jede nicht konfigurierte Integration mit simulierten Daten. Schalte ihn nur ein, um Marshal auszuprobieren oder Screenshots zu machen.

## Verwandte Seiten

::: cards
- [Konfiguration von `demo`](../configuration/marshal-yaml/demo.md)
- [`marshal demo`](../reference/cli.md#marshal-demo)
- [Risiko und Bestätigung](../concepts/risk-and-confirmation.md)
:::
