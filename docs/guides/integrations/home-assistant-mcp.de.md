---
icon: fa-solid fa-house-signal
description: "Lass das Modell dein Zuhause über den MCP-Server von Home Assistant steuern, wobei die Risikoregeln von Marshal entscheiden, welche Aufrufe eine Bestätigung brauchen."
---

# MCP-Server von Home Assistant

Lass das Modell dein Zuhause über den MCP-Server von Home Assistant steuern, wobei die Risikoregeln von Marshal entscheiden, welche Aufrufe eine Bestätigung brauchen.

Marshal nutzt die REST- und Websocket-APIs von HA für seine eigenen Systemaufrufe und den MCP-Server von HA für die Hausteuerung durch das Modell. Der MCP-Server ist mit der Server-ID `ha` konfiguriert.

## Bevor du anfängst

- [Home Assistant ist verbunden](home-assistant.md), mit `ha.url` und `ha.tokenSecret`.
- In HA ist die Integration **Model Context Protocol Server** installiert und auf die API **Assist** eingestellt. Sie bietet Werkzeuge unter `/api/mcp` an. Werkzeugnamen und Endpunkt können je nach HA-Version abweichen, prüfe also, was dein HA meldet.
- Die Entitäten, die Marshal steuern darf, sind für Assist freigegeben. Die Freigabeeinstellungen von HA sind der erste Filter.

## Schritte

::: steps
1. **Füg die Server-Datei hinzu**

   Leg `/config/mcp/ha.yaml` an oder kopiere `deploy/config.example/mcp/ha.yaml`:

   ```yaml [/config/mcp/ha.yaml]
   id: ha
   transport: http
   url: http://homeassistant.local:8123/api/mcp
   authTokenSecret: ha_token
   untrusted: false
   untrustedTools: ["todo__get_items"]
   ```

   `authTokenSecret` ist ein Secret-Name. Es kann dasselbe Secret wie `ha.tokenSecret` sein. Marshal schickt es als Bearer-Token. Jeden Schlüssel findest du unter [MCP-Server](../../configuration/mcp-servers.md).

   MCP-Dateien laden im laufenden Betrieb neu. Ein Neustart ist nicht nötig. Die neuen Werkzeuge erscheinen im nächsten Turn.

2. **Behalte die mitgelieferten Risikoregeln**

   `deploy/config.example/risk.yaml` enthält:

   ```yaml [deploy/config.example/risk.yaml]
   risk:
     - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
       tier: confirm
     - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
       tier: confirm
     - match: { tool: "ha.*" }
       tier: safe
   ```

   Regeln werden der Reihe nach ausgewertet, und der erste Treffer gewinnt. Marshal löst zuerst die Ziele des Aufrufs auf, `ha.intent__HassTurnOff` mit `area = hall` wird also gegen jedes Licht und jedes Schloss im Flur geprüft. Ein Schloss im Flur macht den ganzen Aufruf zu `confirm`.

   > [!IMPORTANT]
   > Externe Werkzeuge, auf die keine Regel passt, haben standardmäßig `confirm`. Ohne die Auffangregel `ha.*` würde jeder HA-Aufruf nach einer Bestätigung fragen. Behalte eine Regel `safe` für die Aufrufe, denen du vertraust.

   Siehe [Risikoregeln](../risk-rules.md).

3. **Markiere Text von Dritten als nicht vertrauenswürdig**

   Aus anderen Apps synchronisierte To-do-Listen enthalten Text, den andere Personen getippt haben. Markiere nur das Werkzeug, das Listen liest:

   ```yaml
   untrustedTools: ["todo__get_items"]
   ```

   Das Lesen einer Liste belastet dann den Durchlauf (Quelle `mcp:ha`), während die Hausteuerung vertrauenswürdig bleibt. `untrustedTools` nimmt Glob-Muster auf den Werkzeugnamen, den HA meldet. Ein Glob-Muster, das auf kein Werkzeug passt, zeigt eine Warnung in `/health` und in der Konfigurationsansicht. Du kannst `untrusted: true` nicht mit `untrustedTools` kombinieren: Marshal weist die Datei ab. Siehe [nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md).

4. **Entscheide über das Werkzeug-Pinning**

   Marshal pinnt die Definition jedes HA-Werkzeugs bei der ersten Erkennung. Die Werkzeugliste von HA ändert sich, wenn du Entitäten freigibst oder Intents hinzufügst, und jedes neue oder geänderte Werkzeug wartet dann unter System > Konfiguration auf deine Freigabe, bevor das Modell es sieht. Ist dir das zu viel Rauschen, schalte das Pinning für diesen Server aus:

   ```yaml
   pinTools: false
   ```

   Das Pinning fängt eine Werkzeugdefinition ab, die sich ändert, nachdem du Marshal eingerichtet hast; die ersten Definitionen beurteilt es nicht. Siehe [Werkzeug-Pinning](../../configuration/mcp-servers.md#werkzeug-pinning).
:::

## Werkzeugnamen

Werkzeuge nutzen die Server-ID als Namensraum, ohne Präfix `mcp.`.

| HA meldet | Interne ID | Name für das Modell |
|---|---|---|
| `intent__HassTurnOn` | `ha.intent__HassTurnOn` | `ha_intent__HassTurnOn` |
| `homeassistant__GetLiveContext` | `ha.homeassistant__GetLiveContext` | `ha_homeassistant__GetLiveContext` |

Zeichen außerhalb von `[a-zA-Z0-9_-]` werden zu `_`. Namen über 64 Zeichen werden mit einem Hash am Ende gekürzt. Risikoregeln passen auf die interne ID.

## Grenzen

- Die Server-ID und `toolPrefix` müssen auf `[a-z][a-z0-9]*` passen. `home_assistant` wird abgewiesen.
- Reservierte eingebaute Namensräume (`mail`, `music`, `timers` und andere) lassen sich nicht als ID oder `toolPrefix` nutzen.
- Ein ausgefallener Server verliert seine Werkzeuge, bis er sich wieder verbindet (mit Backoff). Andere Werkzeuge funktionieren weiter, und `/health` zeigt eine Warnung.
- Eine fehlerhafte Datei wird für sich allein abgewiesen. Andere Server laden.
- Eine Werkzeugliste ändert sich nie mitten in einem Turn.
- Werkzeuge, die der Sprachsatellit verweigert (`denyTools`, `denyConfirm`), sind verborgen, nicht abgelehnt. Siehe [Satelliten](../voice/satellites.md).

## Prüfen, ob es funktioniert

1. Öffne `/health`. Der Server `ha` zeigt keine Warnung.
2. Bitte „schalt das Küchenlicht ein“. Das Licht geht ohne Bestätigung an, wie es die Auffangregel sagt.
3. Bitte „sperr die Haustür auf“. Marshal fragt zuerst nach einer Bestätigung.

## Fehlerbehebung

- **Keine Werkzeuge `ha_`.** Prüfe, dass die URL auf `/api/mcp` endet, die Integration MCP Server auf die API Assist eingestellt und das Token gültig ist.
- **Jeder Aufruf fragt nach einer Bestätigung.** Die Auffangregel `ha.*` fehlt in `risk.yaml`.
- **Warnung für ein Glob-Muster.** Der Werkzeugname, den HA meldet, weicht ab. Lies den genauen Namen in der Konfigurationsansicht.

## Verwandte Seiten

::: cards
- [Home Assistant verbinden](home-assistant.md)
- [Eigene MCP-Server](custom-mcp-servers.md)
- [Referenz der MCP-Server](../../configuration/mcp-servers.md)
- [Risikoregeln](../risk-rules.md)
- [Sichtbarkeit von Werkzeugen](../../concepts/tool-visibility.md)
:::
