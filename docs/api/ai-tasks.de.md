---
icon: fa-solid fa-robot
order: 30
description: "POST /api/v1/ai-tasks führt eine Aufgabe für eine Automation aus und antwortet mit JSON."
---

# AI-Tasks

`POST /api/v1/ai-tasks` führt eine Aufgabe für eine Automation aus und antwortet mit JSON. Die AI-Task-Entität der Home-Assistant-Komponente nutzt den Endpunkt; jeder Client mit dem Scope `aitasks:write` kann es.

Ein AI-Task ist ein gewöhnlicher Durchlauf des Agenten: derselbe Guard, dasselbe Gedächtnis, dieselbe Aufzeichnung und Kostenrechnung wie bei einem Turn einer Unterhaltung. Er läuft in einer neuen Sitzung mit der Quelle `automation` und dem Auslöser `ai_task:<taskName>`, mit dem [Template `ai_task`](../configuration/templates-format.md#eingebaute-templates). Nach der Aufgabe wird die Sitzung geschlossen; nichts setzt sie fort, und sie erscheint nicht im Chatverlauf. Der Endpunkt streamt nicht.

## Anfrage

`multipart/form-data` mit diesen Teilen:

| Teil | Anzahl | Inhalt |
|---|---|---|
| `task` | genau einer | JSON-Objekt, siehe unten |
| `attachment` | 0 bis `aiTasks.maxAttachments` | ein Bild: `image/jpeg`, `image/png` oder `image/webp`, höchstens `aiTasks.maxAttachmentBytes` |

Der Teil `task`:

| Feld | Typ | Pflicht | Beschreibung |
|---|---|---|---|
| `taskName` | Zeichenkette | ja | Name der Aufgabe. Der Auslöser des Durchlaufs ist `ai_task:<taskName>`. |
| `instructions` | Zeichenkette | ja | Was zu tun ist. Gilt wie ein getippter Turn als vertrauenswürdige Eingabe. |
| `structure` | Objekt | nein | JSON Schema der Antwort. Ohne das Feld ist `data` eine Zeichenkette. |
| `language` | Zeichenkette | nein | Sprache der Antwort, zum Beispiel `de` oder `en-GB`. Deutsch und Englisch werden unterstützt; alles andere fällt auf deine bevorzugte Sprache zurück. |

```http
POST /api/v1/ai-tasks HTTP/1.1
Authorization: Bearer <token>
Content-Type: multipart/form-data; boundary=x

--x
Content-Disposition: form-data; name="task"
Content-Type: application/json

{"taskName":"doorbell","instructions":"Who is at the door? Is there a parcel?","language":"en",
 "structure":{"type":"object","properties":{"visitor":{"type":"string"},"parcel":{"type":"boolean"}},"required":["visitor","parcel"]}}
--x
Content-Disposition: form-data; name="attachment"; filename="door.jpg"
Content-Type: image/jpeg

<image bytes>
--x--
```

## Antwort

`200` mit der Antwort und der ID des Durchlaufs (öffne ihn in der Ansicht der Durchläufe):

```json
{ "data": { "visitor": "a courier", "parcel": true }, "runId": "0199…" }
```

Ohne `structure` ist `data` der Antworttext als JSON-Zeichenkette.

### Struktur

Die Schleife läuft ohne Antwortformat, das Modell darf also zuerst Werkzeuge aufrufen. Nach der endgültigen Antwort macht Marshal einen weiteren Modellaufruf, der die Antwort mit dem Schema als Antwortformat in JSON umwandelt, und prüft das Ergebnis. Scheitert das, versucht es noch einmal mit den Validierungsfehlern. Beide Aufrufe werden als Modellaufrufe des Durchlaufs aufgezeichnet.

Die Validierung deckt die Teilmenge ab, die Home Assistant erzeugt: `type` (`object`, `string`, `number`, `integer`, `boolean`, `array`, `null` oder eine Liste davon), `properties`, `required`, `items` und `enum`. Andere Schlüsselwörter werden ignoriert.

### Bilder

Eine Aufgabe mit Anhängen braucht ein Modell mit `vision: true` in der Stufe des Templates. Die Bilder gehen mit den Anweisungen an das Modell. Der Durchlauf ist ab seinem ersten Modellaufruf mit der Quelle `attachment` belastet, Schreibvorgänge in Gedächtnis und Aufgaben brauchen in diesem Durchlauf also eine Freigabe. Der Datensatz des Durchlaufs behält von jedem Bild Medientyp, Größe und SHA-256, nie das Bild.

### Werkzeuge und Freigaben

Das eingebaute Template `ai_task` erlaubt keine Werkzeuge. Erlaubst du in `/config/templates/ai_task.md` Werkzeuge, ist der Durchlauf nicht interaktiv: Ein Aufruf der Bestätigungsstufe oder ein durch Belastung angehobener `persists`-Aufruf geht mit einer Push-Benachrichtigung in die Freigabe-Warteschlange, statt zu laufen. Der Durchlauf geht weiter, und seine Antwort sagt, dass die Aktion wartet. Gibst du sie später frei, führt Marshal genau den eingefrorenen Aufruf aus und schickt dir eine `low`-Benachrichtigung mit dem Ergebnis. Ablehnung oder Ablauf führen nichts aus.

## Fehler

| Status | Code | Ursache |
|---|---|---|
| `400` | `ai_task.invalid_request` | Kein Teil `task`, ungültiges JSON, leeres `taskName` oder `instructions`, oder `structure` ist kein Objekt. |
| `403` | | Dem Token fehlt `aitasks:write`. |
| `413` | `ai_task.too_large` | Zu viele Anhänge, oder einer ist größer als `aiTasks.maxAttachmentBytes`. |
| `415` | `ai_task.unsupported_attachment` | Ein Anhang ist kein JPEG, PNG oder WebP. |
| `422` | `ai_task.no_vision_model` | Anhänge, aber kein Vision-Modell in der Stufe des Templates. Es gibt keinen Modellaufruf. |
| `429` | `ai_task.daily_cap` | `tasks.dailyCostCap` ist erreicht, vor oder während des Durchlaufs. |
| `502` | `ai_task.invalid_output` | Die Antwort passte auch nach dem Reparaturversuch nicht zu `structure`. Der Durchlauf wird mit dem Ergebnis `error` aufgezeichnet. |
| `502` | `ai_task.failed` | Der Durchlauf endete anders, zum Beispiel an der Grenze der Schritte. |
| `504` | `ai_task.timeout` | Der Durchlauf dauerte länger als `aiTasks.timeoutSeconds` und wurde abgebrochen. |

## Verwandte Seiten

::: cards
- [AI-Tasks aus Automationen ausführen](../home-assistant-component/ai-tasks.md)
- [Konfiguration von `aiTasks`](../configuration/marshal-yaml/ai-tasks.md)
- [Authentifizierung und Scopes](authentication.md)
- [Nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md)
:::
