---
icon: fa-solid fa-house-chimney
description: "Verbinde Marshal mit Home Assistant (HA), damit es den Zustand deines Zuhauses liest, einen Live-Cache führt und die unten beschriebenen Funktionen für Timer, Orte, Energie und Kontext ergänzt."
---

# Home Assistant verbinden

Verbinde Marshal mit Home Assistant (HA), damit es den Zustand deines Zuhauses liest, einen Live-Cache führt und die unten beschriebenen Funktionen für Timer, Orte, Energie und Kontext ergänzt.

Die Hausteuerung durch das Modell läuft über den MCP-Server von HA: siehe [MCP-Server von Home Assistant](home-assistant-mcp.md). Diese Seite behandelt die Verbindung selbst.

## Bevor du anfängst

- Ein laufendes Home Assistant, das der Container von Marshal über das Netz erreicht.
- Ein langlebiges Zugriffstoken eines **Administrators**. Die Liste der Entitäten, die für Assist freigegeben sind, braucht Admin-Rechte, und `places.create_here` ebenso.
- In HA sind die Entitäten, die Marshal sehen und steuern darf, für Assist freigegeben (Einstellungen, Sprachassistenten, Freigeben). Andere Entitäten sieht Marshal nie.
- Ein Weg, das Token als Secret bereitzustellen: eine Datei `/run/secrets/ha_token` oder die Variable `MARSHAL_SECRET_HA_TOKEN`. Siehe [Secrets](../secrets.md).

## Schritte

::: steps
1. **Erstelle das Token**

   Öffne in HA dein Profil, Sicherheit, Langlebige Zugangstoken, und erstelle eines namens `marshal`. Kopiere es einmal.

2. **Speichere das Token als Secret**

   ```sh
   printf '%s' '<token>' > ./secrets/ha_token
   ```

   Häng es im Container unter `/run/secrets/ha_token` ein oder übergib es als `MARSHAL_SECRET_HA_TOKEN`. `/config` enthält den Wert nie.

3. **Konfiguriere die Verbindung**

   In `/config/marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   ha:
     url: http://homeassistant.local:8123
     tokenSecret: ha_token
   ```

   `ha.tokenSecret` nennt das Secret. Ist `ha.url` gesetzt und `ha.tokenSecret` fehlt oder löst sich nicht auf, schlägt der Start fehl und nennt den Verweis. Die Schlüssel stehen unter [`ha` in den Abschnitten zu Integrationen](../../configuration/marshal-yaml/integrations.md#ha-home-assistant).

   Starte Marshal nach einer Änderung an `marshal.yaml` neu.

4. **Füg den MCP-Server hinzu**

   Kopiere `deploy/config.example/mcp/ha.yaml` nach `/config/mcp/ha.yaml`. Er nutzt dasselbe Secret. Siehe [MCP-Server von Home Assistant](home-assistant-mcp.md).
:::

## Was dir die Verbindung bringt

- **Registry und Zustands-Cache.** Marshal speichert die Registrys der Entitäten, Geräte, Bereiche und Etagen und die Menge der für Assist freigegebenen Entitäten zwischen. Über ein Websocket-Abonnement hält es die Zustände frisch. Nach einer Neuverbindung lädt es Registrys und Zustände neu, bevor es wieder antwortet. HA hat kein Ereignis für Änderungen der Freigabe, deshalb werden die Registrys außerdem alle fünf Minuten aufgefrischt.
- **Ziel-Resolver.** Bevor ein Aufruf `ha.*` läuft, erweitert Marshal `name`, `area`, `floor`, `domain` und `device_class` zu den konkreten freigegebenen Entitäten. [Risikoregeln](../risk-rules.md) passen auf die aufgelösten Domains und Geräteklassen. Ein Name, der nicht freigegeben ist, oder ein leeres Ergebnis wird als nicht aufgelöst gemeldet.
- **Bereichskontext.** Ein Turn aus der Küche bekommt die Zustände der freigegebenen Küchenentitäten und der Entitäten, die im Text genannt sind, in seinen Prompt. Er bekommt nie alle Zustände. Für mehr ruft das Modell `ha.homeassistant__GetLiveContext` auf.
- **Zeitzone.** Ist `instance.timeZone` nicht gesetzt, ist die Zeitzone der Instanz die in HA konfigurierte Zeitzone.
- **Kanäle für Sprache und Push.** Benachrichtigungen nutzen HA für Durchsagen und Push. Siehe [Benachrichtigungen](../notifications.md).

## Werkzeuge dieser Integration

Alle Werkzeuge sind eingebaut und verborgen, solange `ha.url` nicht gesetzt ist. Die Werkzeuge `ha.*` des MCP-Servers stehen auf [ihrer eigenen Seite](home-assistant-mcp.md).

| Werkzeug | Stufe | Was es tut | Verborgen, wenn |
|---|---|---|---|
| `timers.start(duration, name?)` | safe | Startet einen Timer auf dem Gerät, mit dem du sprichst. `duration` etwa `10m`, `1h30m`, `90s` oder ISO 8601. | der Turn kein Gerät hat oder der MCP-Server von HA `HassStartTimer` anbietet |
| `timers.cancel(name?)` | safe | Bricht einen Timer nach Namen ab, oder den einzigen. | wie oben |
| `timers.status()` | safe | Listet die Timer auf dem Gerät. | wie oben |
| `places.find(query?)` | safe | Listet die Zonen in HA mit Radius, ob du darin bist, und den Zustand, den deine Personen-Entität dort annimmt. | keine Verbindung zu HA |
| `places.create_here(name, radius?)` | confirm | Legt eine Zone an der GPS-Position deiner Personen-Entität an. Radius 50 bis 1000 m, Standard 150. | keine Verbindung zu HA oder keine Personen-Entität für den Eigentümer konfiguriert |
| `energy.usage(period, from?, to?, compareWith?)` | safe | Energie für einen Zeitraum aus dem Energie-Dashboard. | keine Verbindung zu HA oder das Energie-Dashboard hat keine Quellen |

Das Modell sieht sie als `timers_start`, `places_find` und so weiter. Auch die Kalenderwerkzeuge nutzen diese Verbindung: siehe [Kalender](calendar.md).

### Timer

Timer brauchen ein Gerät, sie funktionieren also in Sprach-Turns von einem Satelliten und nicht im Web-Chat. Sie rufen die Intent-Verarbeitung von HA mit der Geräte-ID des Turns auf, damit der Timer an diesem Satelliten klingelt. Bietet der MCP-Server von HA einmal Timer-Intents an, die ohne Gerätekontext nutzbar sind, verwendet Marshal diese und verbirgt `timers.*`.

### Orte

`places.find` und `places.create_here` lesen nur die eigene Personen-Entität des Eigentümers, nie die einer anderen Person. `places.create_here` scheitert, ohne HA aufzurufen, wenn die Personen-Entität keine GPS-Position hat oder eine Zone mit demselben Namen (ohne Beachtung der Groß- und Kleinschreibung) existiert. HA lehnt den Aufruf ab, wenn der Benutzer des Tokens kein Administrator ist, und du siehst die Meldung von HA. Passive Zonen werden gelistet und als passiv markiert, weil eine Personen-Entität ihren Namen nie als Zustand annimmt.

### Energie

`period` ist eines von `today`, `yesterday`, `week` (Montag 00:00 bis jetzt), `month`, `last_week`, `last_month` oder `range` (`from` und `to` als lokale Daten, `to` einschließlich). Grenzen nutzen deine Zeitzone. Das Ergebnis enthält die Quellen, die dein Dashboard konfiguriert: Netzbezug und -einspeisung, Solar, Laden und Entladen der Batterie, Gas, Wasser, Haushaltsverbrauch, Kosten und Vergütung, wenn HA Kostenstatistiken aufzeichnet, und bis zu fünf Top-Geräte. Quellen, die das Dashboard nicht konfiguriert, fehlen, sie sind nicht null. `compareWith: previous` fügt den vorherigen Zeitraum mit gleicher verstrichener Länge hinzu. Offene Zeiträume enden mit der aktuellen vollen Stunde, weil die Langzeitstatistiken von HA stündlich sind.

## Grenzen

- Eine Home-Assistant-Instanz pro Marshal-Instanz.
- Marshal sieht nur Entitäten, die für Assist freigegeben sind.
- Solange der Websocket getrennt ist, scheitern `places.find` und `energy.usage` mit einem Fehler, der sagt, dass Home Assistant nicht verfügbar ist.

## Prüfen, ob es funktioniert

1. Öffne `/health`. Der Eintrag für Home Assistant ist healthy.
2. Frag im Web-Chat „welche Lichter sind in der Küche an?“ Die Antwort kommt aus dem Cache oder aus `ha_homeassistant__GetLiveContext`.
3. Sag an einem Satelliten „stell einen Timer auf zehn Minuten“. Ein Timer startet an diesem Satelliten.
4. Frag „wo ist die Zone Hornbach?“ Das Modell ruft `places_find` auf.

## Fehlerbehebung

- **Der Start schlägt mit `ha.tokenSecret (ha_token)` fehl.** Das Secret löst sich nicht auf. Prüfe den Dateinamen oder die Variable.
- **Entitäten sind nicht aufgelöst.** Die Entität ist in HA nicht für Assist freigegeben.
- **`places.create_here` fehlt.** Der Eigentümer hat keine Personen-Entität als Benachrichtigungsziel eingerichtet.

Mehr unter [Fehlerbehebung](../troubleshooting.md).

## Verwandte Seiten

::: cards
- [MCP-Server von Home Assistant](home-assistant-mcp.md)
- [Kalender](calendar.md)
- [Home Assistant verbinden (erste Schritte)](../../getting-started/connect-home-assistant.md)
- [Risikoregeln](../risk-rules.md)
- [Referenz der Abschnitte zu Integrationen](../../configuration/marshal-yaml/integrations.md)
- [Werkzeug-Referenz](../../reference/tools.md)
:::
