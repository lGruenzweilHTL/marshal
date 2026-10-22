---
icon: fa-solid fa-comment-dots
description: "Spricht Marshal auf einem Satelliten eine Frage, geht deine gesprochene Antwort an die Freigabe, die Aufgabe oder das Ergebnis, zu dem die Frage gehört."
---

# Marshal per Stimme antworten

Spricht Marshal auf einem Satelliten eine Frage, geht deine gesprochene Antwort an die Freigabe, die Aufgabe oder das Ergebnis, zu dem die Frage gehört.

## Bevor du anfängst

- Sprache ist eingerichtet ([Assist-Pipeline](assist-pipeline.md)), und die Home-Assistant-Integration reicht Verweise auf Rückfragen weiter (das tut sie standardmäßig).
- Der Satellit unterstützt `assist_satellite.start_conversation` von Home Assistant.

## Was Rückfragen sind

Marshal setzt `followUp: true` an einer Sprachzustellung oder stellt eine Frage, die eine Antwort erwartet. Dann:

1. speichert es einen Rückfrage-Datensatz (Eigentümer, Nonce, Satellit, Ziel, Ablauf),
2. ruft es `assist_satellite.start_conversation` auf dem Satelliten mit der Nachricht auf,
3. übergibt es `marshal-followup:<nonce>` als zusätzlichen System-Prompt. Die Integration schickt nur die Nonce an Marshal, als `followupRef`.

Deine Antwort wird nur dann mit dem Datensatz verknüpft, wenn die Nonce bekannt, nicht abgelaufen und unbenutzt ist und vom selben Satelliten kommt. Sonst ignoriert Marshal den Verweis und behandelt den Turn normal.

## Die drei Ziele

| Frage zu | Was deine Antwort bewirkt |
|---|---|
| Einer ausstehenden Aktion (Freigabe) | Geht in den normalen Bestätigungsablauf: Ein Bestätigungs-Schlüsselwort oder der Bestätigungsprüfer entscheidet. Sag „bestätigt“ (oder „confirm“). Der Verweis identifiziert nur die Frage. |
| Einer wartenden Aufgabe | Die Antwort wird zu den Auslöser-Nutzdaten der Aufgabe, und die Aufgabe läuft weiter. |
| Einem zugestellten Ergebnis | Startet eine neue Sitzung, die mit dem gesprochenen Text und dem Inhalt des Ergebnisses beginnt. „Erzähl mir mehr über den zweiten Artikel“ läuft in dieser Sitzung, ohne die Aufgabe erneut auszuführen. |

Eine neutrale Antwort auf eine Freigabefrage lässt die Freigabe ausstehen und läuft als normaler Turn.

## Ausprobieren

1. Bitte Marshal um ein Briefing mit einer Sprachzustellung, die eine Antwort erlaubt (siehe [tägliches Briefing](../tasks/daily-briefing.md)).
2. Sag nach dem Briefing „erzähl mir mehr über den zweiten Artikel“.
3. Marshal antwortet aus dem Briefing, ohne es erneut auszuführen.

## Wenn du nicht antwortest

Nichts passiert. Der Datensatz läuft nach `notifications.followUpTtl` ab (Standard 2 Minuten), und die Freigabe oder Aufgabe bleibt, wie sie war. Freigaben bleiben im Eingang offen.

## Rückfallfenster

Reicht ein Satellit oder eine Version von Home Assistant die Nonce nicht weiter, verknüpft Marshal den ersten Sprach-Turn vom selben Satelliten innerhalb von `notifications.followUpReplyWindow` (Standard 20 Sekunden) mit der Frage. Setz es auf `"00:00:00"`, um den Rückfall auszuschalten. Siehe [`notifications`](../../configuration/marshal-yaml/notifications.md).

## Fehlerbehebung

- Die Antwort wird als neue Unterhaltung behandelt: Die Nonce ist nicht angekommen, und die Antwort kam nach dem Fenster. Antworte schneller oder vergrößere das Fenster.
- Eine Antwort von einem anderen Satelliten wird nie verknüpft.

## Verwandte Seiten

::: cards
- [Freigaben](../tasks/approvals.md)
- [Benachrichtigungen](../notifications.md)
- [Risiko und Bestätigung](../../concepts/risk-and-confirmation.md)
:::
