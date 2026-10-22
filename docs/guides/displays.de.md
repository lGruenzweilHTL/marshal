---
icon: fa-solid fa-tv
description: "Lass Marshal Bilder, Videos, Code, Diffs, Markdown und 3D-Modelle auf einem Bildschirm in deinem Zuhause zeigen, und verbinde jeden Bildschirm mit dem Satelliten im selben Raum."
---

# Artefakte auf einem Fernseher zeigen

Lass Marshal Bilder, Videos, Code, Diffs, Markdown und 3D-Modelle auf einem Bildschirm in deinem Zuhause zeigen, und verbinde jeden Bildschirm mit dem Satelliten im selben Raum.

## Bevor du anfängst

- Home Assistant ist verbunden.
- Der Bildschirm ist eine Home-Assistant-Entität `media_player`, zum Beispiel ein Fernseher über die Integration DLNA Digital Media Renderer.
- Der Bildschirm kann Medien von Marshal laden: Du kennst eine Adresse, die er erreicht (siehe Schritt 1).

## Schritte

::: steps
1. **Wähle die Basis-URL für Medien**

   Der Fernseher lädt die Medien selbst, also muss `displays.mediaBaseUrl` eine Adresse sein, die er erreicht. DLNA-Renderer bevorzugen reines HTTP und eine IP-Adresse. Veröffentliche den HTTP-Port von Marshal im LAN, zum Beispiel mit `ports: ["8080:8080"]` in `docker-compose.yml`. Eine HTTPS-URL mit einem Zertifikat, dem der Fernseher vertraut, geht auch. Ohne `mediaBaseUrl` nutzt Marshal `notifications.publicUrl`.

2. **Konfiguriere das Display**

   ```yaml
   displays:
     mediaBaseUrl: http://192.168.1.10:8080/
     linkMinutes: 240
     surfaces:
       living_tv: { entity: media_player.living_room_tv }
   ```

   Namen bestehen aus Kleinbuchstaben, Ziffern und `_` und beginnen mit einem Buchstaben. `linkMinutes` liegt zwischen 5 und 1440. Starte Marshal neu. Der Start schlägt mit dem Namen des Schlüssels fehl, wenn ein Display keine nutzbare Basis-URL, einen ungültigen Namen oder eine Entität außerhalb von `media_player` hat. Siehe [`displays`](../configuration/marshal-yaml/displays.md).

3. **Verbinde das Display mit einem Satelliten**

   In `/config/satellites.yaml`:

   ```yaml [/config/satellites.yaml]
   satellites:
     assist_satellite.living_room:
       display: living_tv
   ```

   Siehe [Satellitenprofile](voice/satellites.md). Das Modell kann kein anderes Display wählen; ein Satellit zeigt nur auf seinem eigenen Display.
:::

## Prüfen, ob es funktioniert

Leg ein Bild in die [Artefakte](artifacts.md) (lade es in der Weboberfläche hoch) und sag dann zum Satelliten im Wohnzimmer: „Zeig das Bild <name> auf dem Fernseher.“ Das Bild erscheint auf dem Bildschirm.

## Wie jede Art aussieht

| Art des Artefakts | Auf dem Display |
|---|---|
| `image` | JPEG und PNG wie gespeichert; GIF und WebP als PNG (erstes Bild). |
| `video` | MP4 wie gespeichert. WebM hat keine Darstellung. |
| `code`, `diff`, `markdown` | PNG-Seiten, 1920x1080, bis zu 40 Zeilen Monospace-Text auf dunklem Hintergrund, mit Name und Seitenzahl in einer Kopfzeile. Syntaxhervorhebung wie im Web-Viewer; Einrückungslinien für Code; grüne und rote Zeilen für hinzugefügte und entfernte Diff-Zeilen. Lange Zeilen brechen um. |
| `model3d` (STL, 3MF) | Zwei PNG-Seiten. Seite 1 ist eine schattierte Ansicht von vorn rechts oben. Seite 2 ist ein Blatt mit Vorder-, Seiten-, Drauf- und perspektivischer Ansicht. Die Kopfzeile zeigt den Begrenzungsrahmen in Millimetern. Farben aus 3MF bleiben erhalten. |
| Andere Arten | Keine Darstellung; Marshal sagt, welche Arten das Display zeigen kann. |

Frag nach einer Seite: „Zeig Seite 2.“ Eine Datei, die sich nicht lesen lässt, keine Dreiecke oder mehr als 5 Millionen Dreiecke hat, hat keine Darstellung.

## Display-Links

Marshal gibt dem Fernseher einen absoluten Link unter der Basis-URL für Medien. Er braucht keine Anmeldung, ist an einen Eigentümer, ein Artefakt und eine Seite gebunden und läuft nach `linkMinutes` ab (Standard 240, lang genug, um ein Video zu pausieren). Abgelaufene, veränderte oder fremde Links bekommen 404. Links unterstützen Range-Anfragen, ein pausiertes Video läuft also weiter.

## Fehlerbehebung

- `display.failed` mit dem Namen des Displays: Home Assistant hat den Aufruf abgelehnt, oft weil der Fernseher aus oder nicht verfügbar ist.
- `ha.unreachable`: Marshal erreicht Home Assistant nicht.
- Der Fernseher zeigt nichts: Er erreicht `mediaBaseUrl` nicht. Teste die Adresse von einem anderen Gerät im LAN.
- `artifacts.no_rendition`: Die Art des Artefakts hat keine Darstellung für Bildschirme.
- `artifacts_show` fehlt in Sprach-Turns: Das Profil des Satelliten hat kein `display`.

## Verwandte Seiten

::: cards
- [Artefakte](artifacts.md)
- [Satelliten](voice/satellites.md)
- [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md)
:::
