---
icon: fa-solid fa-microphone-lines
description: "Mach Marshal zum Konversationsagenten einer Assist-Pipeline in Home Assistant, damit Sprachsatelliten und der Assist-Dialog mit ihm sprechen."
---

# Marshal in einer Assist-Pipeline nutzen

Mach Marshal zum Konversationsagenten einer Assist-Pipeline in Home Assistant, damit Sprachsatelliten und der Assist-Dialog mit ihm sprechen.

## Bevor du anfängst

- Marshal läuft, und Home Assistant erreicht es über HTTP(S). Siehe [Home Assistant verbinden](../../getting-started/connect-home-assistant.md).
- Die Custom-Integration von Marshal ist installiert und eingerichtet. Siehe [Installation der Komponente](../../home-assistant-component/installation.md).
- Das API-Token der Integration hat den Scope `conversation:write`. Siehe [Maschinen-Tokens](../authentication/machine-tokens.md).
- In Home Assistant gibt es eine Engine für Spracherkennung und eine für Sprachausgabe.

## Schritte

::: steps
1. **Prüfe die Konversationsentität von Marshal**

   Nach der Einrichtung hat Home Assistant eine Konversationsentität namens **Marshal**. Sie unterstützt Deutsch und Englisch.

2. **Wähle Marshal in einer Pipeline**

   1. Öffne in Home Assistant **Einstellungen > Sprachassistenten**.
   2. Öffne eine vorhandene Pipeline oder füge eine hinzu.
   3. Setz **Konversationsagent** auf **Marshal**.
   4. Wähle deine Engines für Spracherkennung und Sprachausgabe und die Sprache der Pipeline (`de` oder `en`).
   5. Speichere.

3. **Weise die Pipeline einem Satelliten zu**

   Öffne die Geräteeinstellungen des Satelliten in Home Assistant und wähle die Pipeline. Weise das Gerät des Satelliten einem Bereich zu: Marshal nutzt den Bereich, um „schalt das Licht ein“ auf den Raum aufzulösen, aus dem du sprichst.
:::

## Prüfen, ob es funktioniert

- Sag ein Aktivierungswort und frag „Wie spät ist es?“. Marshal antwortet in der Sprache der Pipeline.
- Tippe eine Nachricht in den Assist-Dialog der Home-Assistant-App. Marshal behandelt sie als Text-Turn.
- Öffne **Läufe** in der Weboberfläche von Marshal. Der Turn erscheint mit seinem Satelliten und Bereich.

## Wie sich ein Turn verhält

| Verhalten | Detail |
|---|---|
| Streaming | Marshal streamt die Antwort, Home Assistant beginnt also zu sprechen, bevor die Antwort vollständig ist. Statusmeldungen werden nie gesprochen. |
| Gesprochener Lückenfüller | Vor einem langsamen Werkzeug, etwa einem Drucker-Snapshot, spricht Marshal eine kurze Zeile („Ich sehe mir den Drucker an.“), wenn noch nichts gesagt wurde. Höchstens einmal pro Turn. Die Schwelle stellst du mit `voice.fillerThresholdMs` ein; siehe [`tools`](../../configuration/marshal-yaml/tools.md). |
| Sprache oder Text | Marshal behandelt Turns, die durch die Spracherkennung gegangen sind, als Sprache, getippte Turns als Text. |
| Latenzgrenze | Ein Sprach-Turn, der `agent.voiceLatencyCap` (Standard 8 Sekunden) überschreitet, bekommt eine kurze Antwort, dass Marshal daran arbeitet. Die Arbeit läuft als Hintergrundaufgabe weiter, und das Ergebnis kommt als Benachrichtigung. Siehe [`agent`](../../configuration/marshal-yaml/agent.md). |
| Fragen | Wenn Marshal dich etwas fragt (eine Bestätigung, eine Klärung), hört der Satellit weiter zu. |
| Fehler | Ist Marshal nicht erreichbar oder schlägt fehl, spricht der Satellit einen kurzen Fehler in der Sprache des Turns. |

## Fehlerbehebung

- „Marshal refused access“: Das Token ist falsch, widerrufen oder hat kein `conversation:write`. Erstelle ein neues Token und konfiguriere die Integration neu.
- „Marshal is not reachable right now“: Prüfe die URL in der Integration und den [Health-Endpunkt](../../reference/health-endpoints.md).

Mehr unter [Fehlerbehebung der Komponente](../../home-assistant-component/troubleshooting.md).

## Verwandte Seiten

::: cards
- [Satelliten](satellites.md)
- [Rückfragen per Stimme](follow-ups.md)
- [Konversations-API](../../api/conversation.md)
- [Wie die Komponente funktioniert](../../home-assistant-component/how-it-works.md)
:::
