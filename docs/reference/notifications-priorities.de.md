---
icon: fa-solid fa-bell
description: "Prioritäten, Kanäle und Regeln für das Routing von Benachrichtigungen sowie die Ergebnisse und Gründe, die Marshal für jede Benachrichtigung aufzeichnet."
---

# Prioritäten von Benachrichtigungen

Prioritäten, Kanäle und Regeln für das Routing von Benachrichtigungen sowie die Ergebnisse und Gründe, die Marshal für jede Benachrichtigung aufzeichnet.

## Prioritäten

| Priorität | Wer sie senden darf | Zustellung |
|---|---|---|
| `critical` | Nur Systemquellen. | Immer per Sprache und Push. Ignoriert Ruhezeiten, Nicht stören, Zusammenfassen und die Obergrenzen für Sprache. |
| `normal` | Alle, auch der Agent. Das ist der Standard. | Nach Anwesenheit geroutet, mit Ruhezeiten, Zusammenfassen und Obergrenzen für Sprache. |
| `low` | Alle, auch der Agent. | Nie per Sprache. Geht in den Posteingang oder als stiller Push raus, wenn die Zustellung Push verlangt. |

Eine Quelle ist eine Systemquelle, wenn ihr Name mit `system:` beginnt. Der Druckwächter (`system:babysitter`) ist eine. `notifications.notify` des Agenten nimmt nur `normal` und `low` an und scheitert sonst mit `notifications.invalid`: „priority is normal or low; only system sources send critical notifications.“ Der Router wendet dieselbe Regel auf jede andere Quelle an.

Erreicht eine kritische Benachrichtigung weder Sprache noch Push (kein Satellit bekannt, kein Push-Ziel), geht sie mit dem Grund `critical_undeliverable` in den Posteingang.

## Kanäle

| Kanal | Verhalten |
|---|---|
| `voice` | Auf einem Satelliten gesprochen. Eine Durchsage oder eine Unterhaltung, wenn eine Antwort erwartet wird (`followUp`). Sprache zielt auf eine Entitäts-ID eines Satelliten oder auf den Raum, in dem du bist. |
| `push` | Eine Push-Benachrichtigung der Companion-App an deine Push-Ziele. |
| `inbox` | Bleibt in deinem Posteingang, bis du oder eine Aufgabe den Eintrag verbraucht. |
| `silent` | Nur für Zustellungen von Aufgaben: keine Benachrichtigung. Das Ergebnis bleibt in der Weboberfläche sichtbar. |

Benachrichtigungen zielen auf eine Person, nicht auf ein Gerät. Offene Browser bekommen außerdem jede Benachrichtigung über den Strom der Live-Ereignisse, egal auf welchem Kanal.

## Wie Marshal eine `normal`-Benachrichtigung routet

Marshal prüft diese Punkte der Reihe nach und nimmt den ersten, der zutrifft.

1. **Posteingang oder still verlangt.** Die Zustellung hat `inbox` (Grund `requested`) oder `silent` verlangt.
2. **Ruhezeiten.** Während deiner Ruhezeiten hält Marshal die Benachrichtigung mit `notifications.quietHoursMode: hold` bis zum Ende der Ruhezeiten zurück (Ergebnis `held`, Grund `quiet_hours`). Mit `silentPush` sendet es sofort einen stillen Push. Eine Aufgabe oder Beobachtung mit `overrideQuietHoursUntil` überspringt diesen Schritt bis zu dieser Zeit.
3. **Zusammenfassen.** Benachrichtigungen an denselben Kanal und dasselbe Ziel innerhalb von `notifications.coalesceWindow` werden zu einer Nachricht. Die erste wartet auf das Ende des Fensters; spätere bekommen das Ergebnis `coalesced`. Benachrichtigungen mit Push-Aktionen oder einer Rückfrage werden nie zusammengefasst.
4. **Wahl des Kanals.**
   - Eine `push`-Zustellung geht per Push.
   - Sonst versucht Marshal Sprache. Nicht stören für Sprache (Grund `dnd`) schaltet Sprache ab. Ein ausdrückliches Ziel gewinnt (Grund `requested`). Sonst entscheidet deine Anwesenheit: Bist du zu Hause in einem belegten Raum, spricht der Satellit dieses Raums (Grund `presence`); bist du weg, geht sie per Push.
   - Marshal überspringt Sprache, wenn dieselbe Quelle schon innerhalb von `notifications.sourceVoiceInterval` gesprochen hat (Grund `source_voice_cap`) oder wenn in der letzten Stunde schon `notifications.voicePerHour` unaufgeforderte Sprachbenachrichtigungen zugestellt wurden (Grund `voice_cap`). Die Benachrichtigung geht dann per Push.
   - Scheitert das Sprechen, geht die Benachrichtigung per Push (Grund `voice_failed`).
5. **Rückfall auf Push.** Ohne Push-Ziel geht die Benachrichtigung in den Posteingang (Grund `no_push_target`). Auch ein gescheiterter Push geht in den Posteingang (Grund `push_failed`).

Die Einstellungen stehen unter [`notifications`](../configuration/marshal-yaml/notifications.md).

## Ergebnisse

Marshal zeichnet jede Benachrichtigung mit Inhalt, Priorität, gewähltem Kanal, Ergebnis und dem Grund der Entscheidung auf.

| Ergebnis | Bedeutung |
|---|---|
| `pending` | Noch nicht entschieden, zum Beispiel weil sie auf das Fenster zum Zusammenfassen wartet. |
| `delivered` | Auf ihrem Kanal zugestellt. |
| `coalesced` | In eine andere Benachrichtigung zusammengefasst. |
| `held` | Bis zum Ende der Ruhezeiten zurückgehalten. |
| `inboxed` | Im Posteingang abgelegt. |
| `expired` | Ihr Zeitfenster für die Zustellung ist vorbei. |
| `fallback` | Auf dem Ersatzkanal der Zustellung zugestellt, nachdem ihre Bedingungen abgelaufen sind. |

## Gründe

| Grund | Zusammen mit | Bedeutung |
|---|---|---|
| `requested` | `delivered`, `inboxed` | Die Zustellung hat diesen Kanal oder dieses Ziel verlangt. |
| `presence` | `delivered` | Sprache, gewählt danach, wo du bist. |
| `no_room` | `delivered` (Push) | Du bist zu Hause, aber kein belegter Raum mit Satellit ist bekannt. |
| `away` | `delivered` (Push) | Du bist nicht zu Hause. |
| `presence_unknown` | `delivered` (Push) | Deine Anwesenheit ist unbekannt. |
| `dnd` | `delivered` (Push) | Nicht stören für Sprache ist an. |
| `quiet_hours` | `held`, `delivered` | Ruhezeiten haben gegriffen. |
| `coalesced` | `coalesced` | In eine frühere Benachrichtigung zusammengefasst. |
| `source_voice_cap` | `delivered` (Push) | Die Quelle hat innerhalb des Intervalls schon gesprochen. |
| `voice_cap` | `delivered` (Push) | Die stündliche Obergrenze für Sprache ist erreicht. |
| `voice_failed` | `delivered` (Push) | Das Sprechen ist gescheitert. |
| `low` | `inboxed`, `delivered` | Niedrige Priorität. |
| `critical` | `delivered` | Kritische Zustellung. |
| `critical_undeliverable` | `inboxed` | Kritisch, aber weder Sprache noch Push war verfügbar. |
| `no_push_target` | `inboxed` | Kein Push-Ziel ist konfiguriert. |
| `push_failed` | `inboxed` | Der Push ist gescheitert. |

## Push mit Aktionen

Push-Benachrichtigungen für Freigaben tragen über die Companion-App von Home Assistant die Aktionen Freigeben und Ablehnen. Die Antwort kommt als Ereignis `mobile_app_notification_action` und zählt nur mit einer gültigen, nicht abgelaufenen Nonce. Eine falsche Nonce gibt nichts frei und wird protokolliert. Siehe [Freigaben](../guides/tasks/approvals.md).

## Verwandte Seiten

::: cards
- [Benachrichtigungen](../guides/notifications.md)
- [Konfiguration von `notifications`](../configuration/marshal-yaml/notifications.md)
- [Werkzeuge](tools.md)
  `notifications.*`
- [Ereignisse](events.md)
:::
