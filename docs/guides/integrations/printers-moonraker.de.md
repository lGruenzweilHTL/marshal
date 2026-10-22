---
icon: fa-solid fa-print
description: "Verbinde einen Klipper-Drucker über Moonraker, damit du nach seinem Status fragen, die Webcam ansehen, Drucke mit Bestätigung steuern und den Druckwächter einen fehlschlagenden Druck pausieren lassen kannst."
---

# Einen Moonraker-Drucker verbinden

Verbinde einen Klipper-Drucker über Moonraker, damit du nach seinem Status fragen, die Webcam ansehen, Drucke mit Bestätigung steuern und den Druckwächter einen fehlschlagenden Druck pausieren lassen kannst.

## Bevor du anfängst

- Ein Drucker mit Klipper und Moonraker, erreichbar aus dem Container von Marshal.
- Der API-Schlüssel von Moonraker, falls er einen verlangt (setz `[authorization]` in `moonraker.conf`). Netze, denen du in `trusted_clients` vertraust, brauchen keinen.
- Für Snapshots und den Druckwächter: eine in Moonraker konfigurierte Webcam (Marshal findet ihre `snapshot_url` über `/server/webcams/list`).
- Für `printer.inspect` und den Druckwächter: ein Modell mit `vision: true` in der Stufe `fast` oder `standard`. Siehe [Modelle](../../installation/models-setup.md).
- Optional: die URL von Mainsail oder Fluidd.

## Schritte

::: steps
1. **Speichere den API-Schlüssel**

   Stell ihn als `/run/secrets/moonraker_voron` oder `MARSHAL_SECRET_MOONRAKER_VORON` bereit. Überspring diesen Schritt, wenn Moonraker keinen Schlüssel braucht.

2. **Konfiguriere den Drucker**

   In `/config/marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   printers:
     - name: voron
       url: http://voron.local:7125
       webUrl: http://voron.local
       apiKeySecret: moonraker_voron
       macros: [PARK, LOAD_FILAMENT, UNLOAD_FILAMENT]
       babysitter: { enabled: true, interval: "00:01:00" }
   printerSnapshots: { maxSize: 1024, quality: 85, retention: 20 }
   ```

   Der Name erlaubt Buchstaben, Ziffern, `-` und `_` und muss eindeutig sein. Jeder Schlüssel steht in der [Referenz der Abschnitte zu Integrationen](../../configuration/marshal-yaml/integrations.md#printers-und-printersnapshots-moonraker). Änderungen brauchen einen Neustart.

3. **Starte neu und öffne die Druckerseite**

   Die Weboberfläche zeigt die Druckerseite mit Live-Status, einem Temperaturdiagramm, Fortschritt, voraussichtlichem Ende, Schicht, Webcam, Druckwarteschlange und Verlauf.
:::

## Werkzeuge

Druckerwerkzeuge nehmen ein optionales Argument `printer`. Bei einem Drucker ist es standardmäßig dieser Drucker. Bei mehreren und ohne Argument liefert das Werkzeug einen Fehler, der die Namen listet. Werkzeuge sind verborgen, solange kein Drucker konfiguriert ist.

| Werkzeug | Stufe | Was es tut |
|---|---|---|
| `printer.status` | safe | Zustand, Auftrag, Fortschritt, Schicht, Restzeit, Temperaturen und den Zustand des Druckwächters. |
| `printer.list_files` | safe | Druckbare Dateien, die neuesten zuerst (höchstens 50). |
| `printer.job_queue` | safe | Die Druckwarteschlange. |
| `printer.history` | safe | Letzte Aufträge, 1 bis 50 (Standard 10). |
| `printer.snapshot` | safe | Webcam-Snapshot als Bild. Ohne Webcam verborgen. |
| `printer.inspect(question)` | safe | Schickt einen Snapshot und deine Frage an ein Vision-Modell. Ohne Webcam oder Vision-Modell verborgen. |
| `printer.pause` | safe | Pausiert den Druck sofort. |
| `printer.emergency_stop` | safe | Hält den Drucker an. Klipper braucht danach einen Neustart. |
| `printer.start_print(file)` | confirm | Startet eine Datei aus der Liste des Druckers. |
| `printer.resume` | confirm | Setzt einen pausierten Druck fort. |
| `printer.cancel` | confirm | Bricht den laufenden Druck ab. |
| `printer.set_temperature(heater, target)` | confirm | `extruder` bis 350 C, `bed` bis 150 C; 0 schaltet ab. Das eigene `max_temp` von Klipper gilt weiterhin. |
| `printer.home(axes)` | confirm | Referenziert `x`, `y`, `z` oder alle Achsen. |
| `printer.move(axis, distance)` | confirm | Relative Bewegung, ungleich null, höchstens 300 mm in jede Richtung. |
| `printer.edit_queue(action, ...)` | confirm | `add`, `remove`, `clear`, `pause`, `start`, `jump`. |
| `printer.run_macro(name)` | confirm | Führt ein Makro aus der Allowlist aus. Verborgen, solange kein Makro konfiguriert ist. |

`pause` und `emergency_stop` sind safe, weil ein zu früh gestoppter Druck wenig kostet und das Warten auf eine Bestätigung mehr. Alles, was heizt, bewegt oder etwas startet, braucht eine Bestätigung. Siehe [Risikostufen](../../reference/risk-tiers.md).

### Makros aus der Allowlist, nie rohes G-Code

Marshal hat kein Werkzeug, das G-Code sendet, Firmware oder Host neu startet oder Dateien löscht. Die einzigen Skripte, die das `/printer/gcode/script` von Moonraker erreichen, baut Marshal aus geprüften Argumenten (Temperatur, Referenzfahrt, relative Bewegung) und aus den Makronamen, die du in `macros` listest.

- `printer.run_macro` nimmt nur Namen aus der konfigurierten Liste `macros` dieses Druckers an, verglichen in Großbuchstaben. Das Namensargument des Werkzeugs ist auf diese Namen beschränkt.
- Ein Makro bekommt nie Argumente.
- Ein Name außerhalb der Liste scheitert an der Validierung, bevor eine Bestätigung gefragt wird.

Liste nur Makros, die nach einer Bestätigung unbeaufsichtigt sicher starten dürfen, etwa `PARK` oder `LOAD_FILAMENT`. Ein Makro führt aus, was deine Klipper-Konfiguration sagt, der Schutz von Marshal ist also nur so gut wie das Makro.

## Druckwächter

Der Druckwächter beobachtet laufende Drucke auf Fehler. Er ist eine Systemkomponente, keine Aufgabe des Agenten. Der Agent kann ihn weder starten noch stoppen noch umkonfigurieren. Nur `babysitter.enabled` in der Konfiguration schaltet ihn ein.

So funktioniert er:

1. Startet ein Druck (oder wird fortgesetzt, oder läuft schon, wenn Marshal startet), macht der Druckwächter alle `interval` einen Webcam-Snapshot. Die erste Prüfung kommt ein Intervall nach dem Start.
2. Ein Vision-Modell (Stufe `fast`, sonst `standard`) beurteilt jeden Snapshot. Es sucht nach Spaghetti, einem gelösten oder umgestoßenen Teil, einem großen Klumpen um die Düse oder Druck in die Luft. Ein dunkles, unscharfes oder unklares Bild ist kein Fehler.
3. Ein positives Urteil bewirkt nichts. **Zwei positive Urteile hintereinander** lösen eine Aktion aus. Ein negatives oder fehlendes Urteil setzt die Zählung zurück.
4. Beim zweiten Treffer pausiert Marshal den Druck direkt, ohne Agentenschleife und ohne Bestätigung, und schickt eine `critical`-Benachrichtigung mit dem Snapshot. Sie wird auch während der Ruhezeiten zugestellt.
5. Die Beobachtung endet, wenn der Druck pausiert, fertig wird, abgebrochen wird, einen Fehler hat oder Klipper herunterfährt. Das Fortsetzen des Drucks startet eine neue Beobachtung.

`printer.status` und die Druckerseite zeigen, ob der Druckwächter aktiv ist, und seine letzten Urteile.

Voraussetzungen: ein `interval` von mindestens 10 Sekunden, eine Webcam und ein Vision-Modell. Jede Prüfung ist ein Modellaufruf, ein kurzes Intervall kostet also Tokens oder GPU-Zeit.

Der Druckwächter pausiert nur. Er bricht nie ab. Du entscheidest, was danach passiert.

## Snapshots

Marshal holt den Snapshot, verkleinert ihn mit SkiaSharp auf `printerSnapshots.maxSize` Pixel, speichert ihn unter `/data/snapshots` und liefert ihn vom Ursprung von Marshal aus. Es behält die neuesten `retention` Snapshots pro Drucker. Die Druckerseite bietet **Behalten**, das den gezeigten Snapshot als Artefakt `image` mit der Quelle `printer` in den Artefaktspeicher kopiert. Behaltene Artefakte werden nicht bereinigt. Siehe [Artefakte](../artifacts.md).

## Die Druckerseite

Bedienelemente auf der Seite rufen direkt die API auf, nicht das Modell, mit denselben Stufen. Sichere Aktionen wirken sofort. Aktionen der Bestätigungsstufe zeigen einen Bestätigungsdialog. Verbotene Aktionen werden nicht angeboten, die Seite verlinkt stattdessen auf `webUrl` (Mainsail oder Fluidd). Jede Bedienaktion wird mit dem Akteur ins Prüfprotokoll geschrieben. Der Browser lädt die Webcam von Marshal, nicht vom Drucker.

## Grenzen

- Der Status kommt aus einem Websocket-Zustands-Cache. Solange der Websocket getrennt ist, scheitern Statuswerkzeuge mit dem Code `printer.offline`. Marshal verbindet sich mit Backoff neu und liest den vollen Zustand erneut.
- Der Zustands-Cache umfasst `print_stats`, `virtual_sdcard`, `extruder`, `heater_bed`, `webhooks` und `display_status`.
- Ein Webcam-Snapshot pro Anfrage; keine Videoanalyse.

## Prüfen, ob es funktioniert

1. Die Druckerseite zeigt Live-Temperaturen.
2. Frag „wie läuft der Druck?“ Das Modell ruft `printer_status` auf.
3. Frag „wie sieht der Druck aus?“ Das Modell ruft `printer_inspect` auf und antwortet aus dem Snapshot.
4. Bitte „heiz die Düse auf 200“. Marshal fragt zuerst nach einer Bestätigung.
5. Starte mit eingeschaltetem Druckwächter einen Druck. `printer_status` meldet den Druckwächter als aktiv.

## Fehlerbehebung

- **`printer.offline`.** Prüfe `url`, den API-Schlüssel und dass `trusted_clients` oder `cors_domains` von Moonraker Marshal erlauben.
- **Kein `printer_inspect`.** Keine Webcam gefunden, oder kein Vision-Modell konfiguriert.
- **`printer_run_macro` fehlt.** `macros` ist leer.

Siehe [Fehlerbehebung](../troubleshooting.md).

## Verwandte Seiten

::: cards
- [Referenz der Abschnitte zu Integrationen](../../configuration/marshal-yaml/integrations.md#printers-und-printersnapshots-moonraker)
- [Benachrichtigungen und die Priorität `critical`](../notifications.md)
- [Risikoregeln](../risk-rules.md)
- [Risikostufen](../../reference/risk-tiers.md)
- [Secrets](../secrets.md)
:::
