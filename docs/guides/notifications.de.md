---
icon: fa-solid fa-bell
description: "Wähle, wo dich proaktive Nachrichten erreichen, wann Marshal still bleibt und wie du siehst, warum eine Nachricht dorthin ging, wo sie ankam."
---

# Steuern, wie Marshal dich benachrichtigt

Wähle, wo dich proaktive Nachrichten erreichen, wann Marshal still bleibt und wie du siehst, warum eine Nachricht dorthin ging, wo sie ankam.

## Bevor du anfängst

- Home Assistant ist verbunden ([Home Assistant verbinden](../getting-started/connect-home-assistant.md)). Sprache braucht Satelliten; Push braucht die Companion-App von Home Assistant.
- Setz `notifications.publicUrl` in `marshal.yaml`, wenn Push-Benachrichtigungen in die Weboberfläche verlinken sollen.

## Schritte

::: steps
1. **Leg deine Benachrichtigungsziele fest**

   In **Einstellungen > Präferenzen**, Abschnitt **Benachrichtigungen**:

   - **Home-Assistant-Person:** deine Entität `person.*`. Marshal erkennt daran, ob du zu Hause bist.
   - **Push-Ziele:** eines pro Zeile. Kritische Warnungen gehen an alle.

2. **Leg Ruhezeiten fest**

   Füge unter **Präferenzen > Ruhezeiten** wöchentliche Zeitfenster mit Tag, Start- und Endzeit in deiner Zeitzone hinzu. Ein Fenster darf über Mitternacht gehen.

3. **Stimme die Lärmkontrolle ab**

   Bearbeite `notifications` in `marshal.yaml`:

   ```yaml [/config/marshal.yaml]
   notifications:
     quietHoursMode: hold            # hold oder silentPush
     coalesceWindow: "00:02:00"
     sourceVoiceInterval: "00:30:00"
     voicePerHour: 6
     followUpReplyWindow: "00:00:20"
     publicUrl: https://marshal.example.com/
   ```

   Liste der Schlüssel: [Referenz von `notifications`](../configuration/marshal-yaml/notifications.md).

4. **Pausiere die Sprache eine Weile**

   Setz **Nicht stören bis** in den Präferenzen. Sprachbenachrichtigungen gehen bis dahin per Push. `critical` ignoriert das.
:::

## Prioritäten

| Priorität | Verhalten |
|---|---|
| `critical` | Immer Sprache und Push, ohne Rücksicht auf Ruhezeiten, Nicht stören und Obergrenzen. Nur Systemquellen (Druckwächter, Not-Halt, Druckerfehler) senden sie. Das Modell kann es nicht: `notifications_notify` nimmt nur `normal` und `low` an. |
| `normal` | Folgt der Kanalwahl, den Ruhezeiten und der Lärmkontrolle. |
| `low` | Nie Sprache. Geht in den Eingang oder als stiller Push, wenn Push angefordert war. |

## Kanäle

- **voice:** eine Durchsage auf einem Satelliten oder eine Unterhaltung, wenn eine Antwort erwartet wird ([Rückfragen](voice/follow-ups.md)).
- **push:** die Companion-App von Home Assistant.
- **inbox:** der Eingang in der Weboberfläche.

Offene Browser bekommen außerdem jede Benachrichtigung über den Live-Stream, egal auf welchem Kanal.

Die Kanalwahl, der Reihe nach: der eigene Kanal der Zustellung und ihre Bedingungen; dann die Anwesenheit (du bist zu Hause: Sprache im Raum, dessen Belegungssensor `binary_sensor.*` an ist; unterwegs: Push); dann Push als Rückfall. Benachrichtigungen gehen an eine Person, nicht an ein Gerät. Eine fehlgeschlagene Sprachzustellung fällt auf Push zurück. Ohne Push-Ziel landet die Benachrichtigung im Eingang.

## Ruhezeiten

Während der Ruhezeiten werden `normal`-Benachrichtigungen bis zum Ende des Fensters zurückgehalten (`quietHoursMode: hold`) oder sofort als stiller Push gesendet (`silentPush`). `low` geht in den Eingang. Eine Aufgabe oder Beobachtung mit einer Ausnahme von den Ruhezeiten, die nur über einen Aufruf der Bestätigungsstufe gesetzt wird, umgeht die Ruhezeiten bis zur angegebenen Zeit.

## Lärmkontrolle

- Mehrere `normal`-Benachrichtigungen innerhalb von `coalesceWindow` werden zu einer Nachricht. Benachrichtigungen mit Aktionen oder einer Rückfrage werden nie zusammengeführt.
- Höchstens eine Sprachbenachrichtigung pro Quelle innerhalb von `sourceVoiceInterval`; weitere gehen per Push.
- Höchstens `voicePerHour` Sprachbenachrichtigungen pro Stunde; darüber hinaus gehen sie per Push.

## Push mit Aktionen

Push-Benachrichtigungen für Freigaben tragen die Aktionen Freigeben und Ablehnen. Die Antwort kommt als Ereignis `mobile_app_notification_action` zurück und zählt nur mit einer gültigen, nicht abgelaufenen Nonce. Eine unbekannte Nonce gibt nichts frei und wird protokolliert. Siehe [Freigaben](tasks/approvals.md).

## Prüfen, ob es funktioniert, und den Grund sehen

Öffne **Eingang** und filtere den Verlauf nach Ergebnis. Jeder Eintrag zeigt Inhalt, Priorität, Kanal, Ergebnis und Grund.

| Ergebnis | Bedeutung |
|---|---|
| `pending` | Wartet, zum Beispiel im Zusammenführungsfenster. |
| `delivered` | Auf einem Kanal gesendet. |
| `coalesced` | In eine andere Nachricht zusammengeführt. |
| `held` | Von den Ruhezeiten zurückgehalten. |
| `inboxed` | In den Eingang gelegt. |
| `expired` | Nicht rechtzeitig zugestellt. |
| `fallback` | Über den Rückfallkanal einer Zustellung zugestellt. |

Eine Nachricht, die wegen der Stundengrenze per Push statt gesprochen kam, hat das Ergebnis `delivered`, den Kanal `push` und den Grund `voice_cap`. Weitere Gründe, die du sehen kannst: `source_voice_cap`, `dnd`, `quiet_hours`, `away`, `no_room`, `presence_unknown`, `voice_failed`, `no_push_target`, `push_failed`.

## Werkzeuge für das Modell

`notifications_notify`, `notifications_inbox_list` und `notifications_inbox_consume` sind Werkzeuge der Stufe safe. Das Briefing listet den Eingang und verbraucht die Einträge, die es abgedeckt hat. Siehe [Werkzeug-Referenz](../reference/tools.md) und [Prioritäten](../reference/notifications-priorities.md).

## Verwandte Seiten

::: cards
- [Geplante Aufgaben](tasks/scheduled-tasks.md)
  Für Zustellungen, Bedingungen und Ablauf
- [Rückfragen per Stimme](voice/follow-ups.md)
- [Benutzerpräferenzen](web-ui/language.md)
:::
