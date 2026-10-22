---
icon: fa-solid fa-music
description: "Lass Marshal Musik auf benannten Lautsprechern suchen, abspielen, einreihen, verschieben und gruppieren, mit dem Lautsprecher des fragenden Satelliten als Standard."
---

# Musik auf Lautsprechern mit Music Assistant abspielen

Lass Marshal Musik auf benannten Lautsprechern suchen, abspielen, einreihen, verschieben und gruppieren, mit dem Lautsprecher des fragenden Satelliten als Standard.

Marshal tut das über Home Assistant und die Integration Music Assistant. Es spricht nicht direkt mit Music Assistant.

## Bevor du anfängst

- Eine funktionierende [Verbindung zu Home Assistant](home-assistant.md) (`ha.url` gesetzt).
- Die Integration Music Assistant in Home Assistant, mit mindestens einem Player, der als Entität `media_player` erscheint.
- Die Entitäts-IDs dieser Player, zum Beispiel `media_player.kitchen_satellite_ma`.

> [!IMPORTANT]
> Gib die Player von Music Assistant nicht für Assist frei. Die eigenen Medien-Intents von Home Assistant bieten dann einen zweiten Weg, denselben Lautsprecher zu pausieren oder abzuspielen, und kleine Modelle wählen zufällig zwischen beiden.

## Schritte

::: steps
1. **Benenne deine Lautsprecher**

   Füge `/config/marshal.yaml` einen Abschnitt `music` hinzu. Jeder Schlüssel ist ein Lautsprechername, jeder Wert eine Entität `media_player` von Music Assistant.

   ```yaml
   music:
     speakers:
       kitchen:     media_player.kitchen_satellite_ma
       living_room: media_player.living_room_sonos
   ```

   Lautsprechernamen passen auf `[a-z][a-z0-9_]*`. Der Start schlägt mit einem Fehler fehl, der `music.speakers.<name>` nennt, wenn ein Name nicht passt oder die Entität nicht in der Domain `media_player` liegt. Das Modell nutzt nur diese Namen und nie eine Entitäts-ID.

   Ohne Abschnitt `music` oder ohne Verbindung zu Home Assistant registriert Marshal kein Werkzeug `music_*`.

2. **Setz den Standardlautsprecher pro Satellit**

   Gib in `/config/satellites.yaml` jedem Satellitenprofil den Lautsprecher, auf dem es spielt:

   ```yaml [/config/satellites.yaml]
   # in einem Satellitenprofil
   speaker: kitchen
   ```

   Ein Musikwerkzeug ohne Lautsprecher nutzt dann den Lautsprecher des Satelliten, der gefragt hat. Siehe [Satelliten](../voice/satellites.md) und [`satellites.yaml`](../../configuration/satellites-yaml.md).

3. **Starte Marshal neu**

   ```sh
   docker compose restart marshal
   ```
:::

## Werkzeuge

Alle Musikwerkzeuge sind in der Stufe safe. Jedes scheitert mit `ha.unreachable`, wenn Home Assistant nicht erreichbar ist, und mit `music.failed` samt Lautsprechername, wenn Home Assistant oder Music Assistant den Aufruf ablehnt.

| Werkzeug | Was es tut |
|---|---|
| `music_search(query, type?)` | Bis zu 10 Ergebnisse pro Typ (`track`, `album`, `artist`, `playlist`, `radio`) mit Name, Künstlern, Album und URI von Music Assistant. |
| `music_play(item, type?, speaker?, enqueue?, radio?)` | Spielt eine Suchphrase wie `Queen - Innuendo` oder eine URI aus `music_search`. |
| `music_queue(speaker?)` | Wiedergabezustand, aktueller und nächster Titel, Zahl der Titel, Zufalls- und Wiederholmodus. |
| `music_control(action, speaker?, volume?)` | `pause`, `resume`, `next`, `previous`, `stop` oder `volume` mit einem Wert von 0 bis 100. |
| `music_transfer(to, from?)` | Verschiebt Warteschlange und Wiedergabe von `from` (Standard: der Lautsprecher des Turns) nach `to`. |
| `music_group(speakers)` | Fasst die gelisteten Lautsprecher zu einer synchronen Gruppe zusammen, angeführt vom ersten. |
| `music_ungroup(speaker)` | Nimmt einen Lautsprecher aus seiner Gruppe. |

`enqueue` in `music_play` ist `play` (Standard), `replace`, `next`, `replace_next` oder `add`. `radio: true` macht mit ähnlicher Musik weiter.

### Welchen Lautsprecher ein Aufruf nutzt

- Ein Aufruf ohne `speaker` nutzt den Lautsprecher aus dem Satellitenprofil des Turns.
- Ein Aufruf ohne `speaker` in einem Turn, der keinen hat, etwa einem Web-Chat-Turn, scheitert mit `music.no_speaker` und listet die konfigurierten Namen.
- Ein unbekannter Name scheitert mit `music.unknown_speaker` und listet die konfigurierten Namen.

## Grenzen

- `music_play` weist eine URL als `item` (`http`, `https`, `ftp`, `file`, `rtsp` und jedes andere Schema mit folgendem Host) mit `music.invalid_arguments` ab, bevor es Home Assistant aufruft. URIs von Music Assistant wie `library://album/12` und einfache Suchphrasen funktionieren. Marshal spielt nie eine Webadresse, die das Modell gewählt hat.
- Eine Lautstärke außerhalb von 0 bis 100 wird mit `music.invalid_arguments` verweigert, und nichts wird gesendet.
- Namen von Musikanbietern sind Text von Dritten. Die Ausgabe von `music_search` und `music_queue` ist nicht vertrauenswürdig: Ein Durchlauf, der eines davon aufruft, ist mit der Quelle `music` belastet. Siehe [nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md).

## Prüfen, ob es funktioniert

1. Frag in der Weboberfläche „Such nach Innuendo“. Die Antwort listet Titel und Alben mit URIs.
2. Bitte „Spiel das Album auf kitchen“. Der Küchenlautsprecher beginnt zu spielen. Ein Web-Turn ohne Lautsprechername bekommt eine Antwort `music.no_speaker`, die deine Lautsprecher listet, was zeigt, dass die Zuordnung funktioniert.
3. Bitte einen Satelliten „Spiel etwas von Queen“. Die Wiedergabe startet auf dem eigenen Lautsprecher des Satelliten.
4. Bitte „Verschieb die Musik ins Wohnzimmer“. Die Warteschlange läuft auf `living_room` weiter.

## Verwandte Seiten

::: cards
- [Home Assistant](home-assistant.md)
- [`music` in marshal.yaml](../../configuration/marshal-yaml/integrations.md)
- [Sprachsatelliten](../voice/satellites.md)
- [Werkzeug-Referenz](../../reference/tools.md)
- [Medienanfragen](media.md)
:::
