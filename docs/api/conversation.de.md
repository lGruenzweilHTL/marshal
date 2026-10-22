---
icon: fa-solid fa-comments
order: 20
description: "POST /api/v1/conversation/turns führt einen Turn einer Unterhaltung aus und streamt die Antwort als Server-Sent Events."
---

# Unterhaltungsendpunkt

`POST /api/v1/conversation/turns` führt einen Turn einer Unterhaltung aus und streamt die Antwort als Server-Sent Events. Die Home-Assistant-Komponente nutzt ihn für jeden Assist-Turn; du kannst ihn aus jedem Client nutzen.

## Anfrage

Braucht den Scope `conversation:write` (siehe [Authentifizierung](authentication.md)). Der Body ist JSON.

| Feld | Typ | Pflicht | Beschreibung |
|---|---|---|---|
| `text` | Zeichenkette | ja | Was der Benutzer gesagt oder getippt hat. Leer oder nur Leerraum gibt `400` `conversation.text_required` zurück. |
| `conversationId` | Zeichenkette | Token-Clients: ja | Token-Clients schicken ihre eigene Unterhaltungs-ID und nutzen sie für Folge-Turns wieder. Browser-Sitzungen lassen sie weg, um eine Unterhaltung zu beginnen, oder schicken Marshals Sitzungs-ID aus einem früheren `final`-Ereignis. |
| `language` | Zeichenkette | nein | `de` oder `en`. Die Antwort nutzt sie. |
| `inputMode` | Zeichenkette | nein | `voice`, wenn der Text aus Speech-to-Text kommt, sonst `text` (Standard). |
| `deviceId` | Zeichenkette | nein | Geräte-ID in Home Assistant. |
| `satelliteId` | Zeichenkette | nein | Entitäts-ID des Satelliten, zum Beispiel `assist_satellite.kitchen`. Wählt das [Satellitenprofil](../configuration/satellites-yaml.md). |
| `areaId` | Zeichenkette | nein | Bereichs-ID des Satelliten. |
| `haUserId` | Zeichenkette | nein | Benutzer-ID in Home Assistant, falls bekannt. |
| `followupRef` | Zeichenkette | nein | Nonce aus einer [Rückfrage per Sprache](../guides/voice/follow-ups.md). Unbekannte oder abgelaufene Werte werden ignoriert. |

```json
{
  "conversationId": "01JABC",
  "text": "How far along is the print?",
  "language": "en",
  "inputMode": "text"
}
```

::: request v1 CreateConversationTurn
body:
  conversationId: "01JABC"
  text: How far along is the print?
  language: en
  inputMode: text
:::

Ein Token-Client ohne `conversationId` bekommt `400` `conversation.id_required`. Eine Browser-Sitzung, die eine Sitzungs-ID schickt, die ihr nicht gehört oder die beendet ist, bekommt `404` `session.not_found`, und kein Durchlauf startet.

Ein Turn mit `satelliteId`, `deviceId` oder `haUserId` wird als Sprachsitzung aufgezeichnet, sonst als API-Sitzung.

## Antwort

`200` mit `Content-Type: text/event-stream`. Jedes Ereignis hat einen Namen und eine JSON-Zeile `data`. Schick `Accept: text/event-stream`.

| Ereignis | Daten | Wann |
|---|---|---|
| `delta` | `{ "text": "…" }` | Null- oder mehrmals. Ein Stück des Antworttexts. In einem Sprach-Turn kann das erste Delta ein kurzer gesprochener Überbrückungssatz sein, der vor einem langsamen Werkzeug gesendet wird; er gehört zu `speech`. |
| `status` | `{ "text": "…" }` | Optional. Eine Fortschrittsmeldung, zum Beispiel wenn ein Turn in den Hintergrund wechselt. Sprich sie nicht aus. |
| `artifact` | `{ "id", "name", "kind", "url" }` | Einmal pro Artefakt, das der Turn zeigt. `url` ist die URL des Inhalts. |
| `reasoning` | `{ "text": "…" }` | Nur Browser-Sitzungen. Null- oder mehrmals. Ein Stück des Reasonings des Modells. |
| `tool` | `{ "id", "tool", "arguments", "state", "durationMs", "result" }` | Nur Browser-Sitzungen. Zweimal pro Werkzeugaufruf: beim Start (`state` `running`) und am Ende (gleiche `id`, `state` `ok`, `error`, `denied` oder `pending`, mit `durationMs` und optionalem `result`). |
| `widget` | `{ "kind", "toolCallId", "data" }` | Nur Browser-Sitzungen. Nach einem erfolgreichen Aufruf eines Werkzeugs mit eingebautem Widget: `kind` `printer` (aus `printer.status`) oder `music` (aus `music.queue`, `music.play`). `data` ist der Anfangszustand des Widgets. |
| `app` | `{ "appId", "server", "tool", "toolCallId" }` | Nur Browser-Sitzungen. Nach einem Aufruf eines Werkzeugs eines MCP-Servers mit App (`apps: true`). Lade sie von `GET /api/v1/apps/{appId}`; ihr HTML liegt unter `/mcp-apps/{appId}`. |
| `final` | `{ "speech", "continueConversation", "conversationId" }` | Genau einmal, als letztes. |

Token-Clients (Home Assistant, API) bekommen nie `reasoning`-, `tool`-, `widget`- oder `app`-Ereignisse. Ihr Strom bleibt unverändert.

Die Felder des `tool`-Ereignisses:

| Feld | Beschreibung |
|---|---|
| `id` | Die Aufruf-ID. Das abschließende Ereignis wiederholt sie. |
| `tool` | Die Werkzeug-ID, zum Beispiel `printer.status`. |
| `arguments` | Eine einzeilige Zusammenfassung der Argumente (Paare `key=value`, nach 120 Zeichen abgeschnitten). |
| `state` | `running`, `ok`, `error`, `denied` (der Guard hat abgelehnt) oder `pending` (wartet auf Bestätigung). |
| `durationMs` | Wie lange der Aufruf gedauert hat. Nur bei abschließenden Ereignissen. |
| `result` | Die erste Zeile des Ergebnisses (nach 120 Zeichen abgeschnitten) oder der Fehlercode. Fehlt bei sensiblen und geschwärzten Werkzeugen. |

Die Felder des `final`-Ereignisses:

| Feld | Beschreibung |
|---|---|
| `speech` | Der vollständige Antworttext. Gleich den aneinandergehängten Deltas oder die ganze Antwort, wenn nichts gestreamt wurde. |
| `continueConversation` | `true`, wenn Marshal etwas gefragt hat (eine Bestätigung oder Rückfrage) und der Client weiter zuhören soll. |
| `conversationId` | Token-Clients: die ID, die du geschickt hast. Browser-Sitzungen: Marshals Sitzungs-ID. |

```text
event: delta
data: {"text":"The print is "}

event: delta
data: {"text":"at 42 percent."}

event: final
data: {"speech":"The print is at 42 percent.","continueConversation":false,"conversationId":"01JABC"}

```

Validierungsfehler (`400`, `404`, `403`) kommen als [Problem Details](index.md#fehler), bevor der Strom beginnt. Hat der Strom begonnen, lies bis `final`. Ein Strom, der ohne `final` endet, ist ein Fehler.

## Latenzgrenze

Jeder Turn hat eine harte Latenzgrenze: `agent.voiceLatencyCap` (Standard 8 Sekunden) für Sprach-Turns und `agent.textLatencyCap` (Standard 60 Sekunden) für Text-Turns. Erreicht ein Turn die Grenze, schickt Marshal eine kurze Antwort, dass es daran arbeitet und sich wieder meldet, beendet den Turn und erledigt die Arbeit als Hintergrundaufgabe desselben Benutzers. Ergebnisse von Sprach-Turns kommen auf dem ursprünglichen Satelliten an, Ergebnisse von Text-Turns in der Weboberfläche. Das Modell kann einen Turn auch selbst in den Hintergrund verschieben.

Die Hintergrundaufgabe merkt sich die Sitzung, aus der sie kommt. Ihr Ergebnis wird Teil dieser Unterhaltung: Der Chatverlauf im Web listet es nach dem Turn, der in den Hintergrund ging, der offene Chat zeigt es live (`conversation.result` im [Strom der Live-Ereignisse](live-events.md)), und das Modell sieht es im nächsten Turn der Sitzung als frühere Antwort.

Setz die Timeouts des Clients über die Grenze für Text. Die Home-Assistant-Komponente erlaubt insgesamt 75 Sekunden. Siehe [Konfiguration von `agent`](../configuration/marshal-yaml/agent.md).

## Bestätigungen

Endet ein Turn mit einer ausstehenden Aktion der Bestätigungsstufe, ist `continueConversation` `true`. Der nächste Turn des Benutzers (gleiche `conversationId`) beantwortet die Frage. Marshal friert die Aktion auf dem Server ein; nur ein Turn des Benutzers, eine Freigabe im Web oder eine geprüfte Push-Aktion bestätigt sie. Siehe [Risiko und Bestätigung](../concepts/risk-and-confirmation.md).

## Verwandte Seiten

::: cards
- [Beispiele](examples.md)
- [Wie die Home-Assistant-Komponente Turns behandelt](../home-assistant-component/how-it-works.md)
:::
