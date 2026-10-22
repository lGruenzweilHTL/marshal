---
icon: fa-solid fa-gears
order: 30
description: "Was die Komponente bei jedem Assist-Turn an Marshal schickt, wie sie mit der Antwort umgeht und wie sich Zeitlimits, Fehler und Rückfragen verhalten."
---

# Wie die Home-Assistant-Komponente funktioniert

Diese Seite beschreibt, was die Komponente bei jedem Assist-Turn an Marshal schickt, wie sie mit der Antwort umgeht und wie sich Zeitlimits, Fehler und Rückfragen verhalten.

## Ein Turn

1. Assist übergibt den Turn an die Konversationsentität von Marshal.
2. Die Komponente schickt `POST /api/v1/conversation/turns` mit den Feldern unten und liest den [Ereignisstream](../api/conversation.md).
3. `delta`-Ereignisse gehen als gestreamter Assistenteninhalt in das Chat-Protokoll von Home Assistant, damit eine streamende Sprachausgabe beginnen kann, bevor die Antwort vollständig ist.
4. Das Ereignis `final` setzt die gesprochene Antwort und `continue_conversation`.

`status`-Ereignisse werden nie gesprochen. Kamen keine Deltas an, dient die `speech` aus `final` als Antwort.

## Gesendeter Kontext

Die Komponente schickt nur Felder des Turns. Sie schickt keine Entitätszustände und keine anderen Daten von Home Assistant; Marshal liest, was es braucht, selbst aus Home Assistant.

| Feld in Marshal | Quelle in Home Assistant |
|---|---|
| `text` | Der Text des Benutzers |
| `language` | Primäres Subtag der Sprache des Turns (`de-AT` wird zu `de`) |
| `conversationId` | Die Unterhaltungs-ID von Home Assistant (die Komponente erzeugt eine, wenn keine existiert, und gibt sie zurück) |
| `deviceId` | Die Geräte-ID des Turns |
| `satelliteId` | Die Entitäts-ID des Satelliten |
| `areaId` | Bereich des Geräts; ohne Bereich der Bereich der Satellitenentität oder ihres Geräts |
| `haUserId` | Die Benutzer-ID des Kontexts |
| `inputMode` | `voice`, wenn der Pipeline-Lauf beim Aktivierungswort oder bei der Spracherkennung begann, `text`, wenn getippt |
| `followupRef` | Nonce aus einer Rückfrage-Markierung, siehe unten |

Der Eingabemodus wird aus der laufenden Assist-Pipeline gelesen. Lässt er sich nicht lesen, zählt ein Turn mit Satelliten-ID als Sprache und alles andere als Text.

Marshal nutzt `satelliteId` und `areaId` für den Raumkontext und das [Satellitenprofil](../configuration/satellites-yaml.md) (Ausführlichkeit, Notizen, verweigerte Werkzeuge).

## Zeitlimits

| Schritt | Grenze |
|---|---|
| Verbindungsaufbau | 5 s |
| Ganzer Turn | 75 s |
| Prüfungen von Version und Token | 10 s |

Marshal setzt pro Turn seine eigene Latenzgrenze durch (`agent.voiceLatencyCap`, Standard 8 s; `agent.textLatencyCap`, Standard 60 s). Wird sie erreicht, spricht Marshal eine kurze Antwort, dass es daran arbeitet, und macht im Hintergrund weiter; das Ergebnis kommt später als [Benachrichtigung](../guides/notifications.md). Das Zeitlimit der Komponente ist ein Sicherheitsnetz über der Grenze für Text.

## Eine Unterhaltung fortsetzen

Stellt Marshal eine Frage, zum Beispiel um eine Aktion zu bestätigen, hat das Ereignis `final` den Wert `continueConversation = true`, und die Komponente gibt das an Home Assistant weiter, damit der Satellit weiter zuhört. Marshal entscheidet das; ein Fragezeichen am Ende allein reicht nicht.

## Rückfragen

Fragt Marshal von sich aus per Stimme etwas (zum Beispiel bei einem zugestellten Ergebnis oder einer Freigabefrage), ruft es `assist_satellite.start_conversation` mit dem zusätzlichen System-Prompt `marshal-followup:<nonce>` auf. Kommt die gesprochene Antwort, findet die Komponente die Markierung und schickt die Nonce als `followupRef`. Sonst gibt sie nichts aus dem zusätzlichen System-Prompt weiter. Marshal verknüpft die Antwort mit der ausstehenden Aktion, Aufgabe oder dem Ergebnis, wenn die Nonce bekannt, nicht abgelaufen, unbenutzt und vom selben Satelliten ist; sonst behandelt es einen normalen Turn. Kommt keine Antwort, passiert nichts, und die Rückfrage läuft ab. Siehe [Rückfragen](../guides/voice/follow-ups.md).

## Fehler

Ist Marshal nicht erreichbar, überschreitet das Zeitlimit, lehnt das Token ab oder liefert einen Fehler, wirft die Komponente keine Ausnahme. Sie spricht einen kurzen Fehler in der Sprache des Turns (Deutsch oder Englisch; andere Sprachen bekommen Englisch) und protokolliert die Details als Warnung.

| Fall | Deutsche Meldung |
|---|---|
| Nicht erreichbar oder Zeitüberschreitung | Marshal ist gerade nicht erreichbar. |
| `401` oder `403` | Marshal hat den Zugriff abgelehnt. Prüfe das API-Token der Integration. |
| Anderer Fehler, oder Stream ohne Ereignis `final` | Marshal konnte das gerade nicht bearbeiten. |

## Verwandte Seiten

::: cards
- [Konversationsendpunkt](../api/conversation.md)
- [Fehlerbehebung](troubleshooting.md)
- [Datenschutz](../concepts/privacy.md)
:::
