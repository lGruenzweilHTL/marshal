---
icon: fa-solid fa-clock
description: "Leg Aufgaben an, die jetzt, zu einer Uhrzeit, nach einem Cron-Zeitplan oder nach einer anderen Aufgabe laufen, und steuere, wohin ihre Ergebnisse gehen."
---

# Geplante Aufgaben

Leg Aufgaben an, die jetzt, zu einer Uhrzeit, nach einem Cron-Zeitplan oder nach einer anderen Aufgabe laufen, und steuere, wohin ihre Ergebnisse gehen.

## Bevor du anfängst

- Eine funktionierende Unterhaltung mit Marshal (Sprache, Web-Chat oder API). Aufgaben legt der Agent über die Werkzeuge `tasks.*` an; es gibt kein Formular zum Anlegen in der Weboberfläche, und Marshal liest kein Verzeichnis `/config/tasks` (siehe [Aufgabendefinitionen](../../configuration/tasks-format.md)).
- Deine Zeitzone ist gesetzt (`instance.timeZone` oder dein Profil). Cron-Ausdrücke und `at`-Zeiten werden in der Zeitzone des Eigentümers ausgewertet und überstehen Zeitumstellungen.
- Für Zustellungen per Sprache oder Push: ein Satellit oder die Home-Assistant-App, siehe [Benachrichtigungen](../notifications.md).

## Schritte

::: steps
1. **Bitte um die Aufgabe**

   Beschreib das Ziel und die Zeit. Marshal ruft `tasks.create` auf.

   ```text
   Prüf an jedem Werktag um 7:30 die Druckerwarteschlange und sag mir, was wartet. Nenn die Aufgabe „queue-check“.
   ```

   Oder übergib die Argumente direkt, wie in den Beispieldateien:

   ```yaml
   name: queue-check
   goal: Check the printer queue and report what is waiting.
   trigger:
     kind: cron
     expression: "30 7 * * 1-5"
   deliver:
     - channel: inbox
   ```

2. **Wähle einen Auslöser**

   | `trigger.kind` | Zusätzlicher Schlüssel | Verhalten |
   |---|---|---|
   | `now` (Standard) | keiner | Läuft, sobald ein Worker frei ist. |
   | `at` | `time` | Läuft einmal zu einer lokalen Zeit wie `2026-09-30T07:00`. Füge einen Offset hinzu (`+02:00`, `Z`), um eine absolute Zeit festzulegen. |
   | `cron` | `expression` | Cron-Ausdruck mit fünf Feldern in lokaler Zeit. Die Aufgabe wartet nach jedem Durchlauf auf den nächsten Termin. |
   | `after` | `task` | Läuft, sobald die genannte Aufgabe (ID oder Name) mit `done` endet. |

   Ein Cron-Zeitplan darf höchstens alle `tasks.minWakeupInterval` laufen (Standard 5 Minuten). Ereignis-Auslöser sind unter [Ereignis-Auslöser](event-triggers.md) beschrieben.

3. **Wähle die Zustellungen**

   `deliver` ist eine Liste; jeder Eintrag wird unabhängig zugestellt. Ohne `deliver` bleibt das Ergebnis in der Weboberfläche.

   | Schlüssel | Beschreibung |
   |---|---|
   | `channel` | `voice`, `push`, `inbox` oder `silent`. |
   | `target` | Nur Sprache: Entitäts-ID des Satelliten. Weglassen, um dort zu sprechen, wo du bist. |
   | `priority` | `normal` (Standard) oder `low`. |
   | `followUp` | Nur Sprache: Der Satellit hört auf eine Antwort. |
   | `when` | Liste von Bedingungen `state(<entity_id> <op> <value>)`, alle müssen gelten. Operatoren `=`, `!=`, `>`, `<` (numerisch). `$person` ist deine Personen-Entität. |
   | `expires` | Lokale Frist, `11:00` oder `2026-09-30T11:00`. Braucht `fallback`. |
   | `fallback` | Kanal, der genutzt wird, wenn `expires` zuerst verstreicht. Braucht `expires`. |

   Sprache nutzt die `speech` des Ergebnisses, Push nutzt `speech` plus einen Deep Link, die Weboberfläche zeigt `body`. Eine Zustellung mit Bedingungen geht hinaus, sobald die Bedingungen zum ersten Mal gelten; verstreicht vorher `expires`, stellt stattdessen der Rückfallkanal zu. Marshal weist diese Kombinationen ab:

   | Fehlertext | Ursache |
   |---|---|
   | `Low-priority deliveries never use voice; use inbox or push.` | `priority: low` mit `channel: voice` (oder Rückfall auf Sprache). |
   | `followUp needs the voice channel.` | `followUp: true` auf einem anderen Kanal. |
   | `Only voice deliveries have a target.` | `target` auf einem Kanal, der nicht Sprache ist. |
   | `A delivery with expires needs a fallback channel.` / `A fallback channel needs expires.` | Einer der beiden fehlt. |
   | `A delivery cannot be critical; only system sources send critical notifications.` | Die Priorität `critical` steht Aufgaben nie zur Verfügung. |

   Eine Bedingung, die einen Bereich nennt (`area.kitchen`), wird abgewiesen: Bedingungen nehmen nur Entitäts-IDs. Ruhezeiten halten normale Benachrichtigungen zurück; siehe [Benachrichtigungen](../notifications.md).

4. **Weck eine Aufgabe später**

   Innerhalb eines Durchlaufs kann der Agent `tasks.schedule_wakeup` aufrufen (`at` oder `inMinutes`, plus `notes`). Der Durchlauf endet mit dem Status `waiting`; der nächste Durchlauf bekommt das Ziel, die Notizen und die Auslöser-Nutzdaten, nicht das vorherige Transkript. Die Verzögerung muss mindestens `minWakeupInterval` des Templates sein (Standard `tasks.minWakeupInterval`): `A wakeup must be at least 5 minutes ahead.`

5. **Verwalte Aufgaben**

   | Werkzeug | Was es tut |
   |---|---|
   | `tasks.list`, `tasks.status` | Aufgaben auflisten; eine nach ID oder Name mit ihrem letzten Ergebnis und dessen Alter zeigen. |
   | `tasks.update` | Ziel, Auslöser, Zustellungen, Template oder Name ändern. Eigentümer, Budget und Ursprungssatellit lassen sich nicht ändern. |
   | `tasks.run_now` | Eine vorhandene Aufgabe jetzt mit ihrem normalen Budget und ihren Zustellungen ausführen. |
   | `tasks.cancel` | Eine Aufgabe und ihre unfertigen Kindaufgaben abbrechen. |
   | `tasks.stop_all` | Not-Aus: alles abbrechen und neue Starts pausieren. |

   Aufgabennamen bestehen aus Kleinbuchstaben, Ziffern, `-` und `_`, höchstens 64 Zeichen, eindeutig pro Eigentümer (`A task named 'briefing' already exists.`).
:::

## Vorbereiten

Eine geplante Aufgabe kann ihr langsames Lesen erledigen, bevor sie fällig ist. Bitte darum, wenn du die Aufgabe anlegst, zum Beispiel „bereite Kalender und Nachrichten drei Stunden vorher vor“. Marshal übergibt `prepare: { before: 3h, goal: ... }` an `tasks.create` oder `tasks.update`.

Das Detail der Aufgabe in der Weboberfläche zeigt das Vorbereitungsziel, die Vorlaufzeit, den nächsten Vorbereitungsdurchlauf und ob vorbereitete Notizen warten. Schlüssel, Grenzen und was mit veralteten Notizen passiert: [Vorbereitung](../../configuration/tasks-format.md#vorbereitung).

## Werkzeugrichtlinie

Jede Aufgabe darf nur die Werkzeuge ihrer Werkzeugrichtlinie nutzen. Legst du eine Aufgabe aus einer Unterhaltung an, besteht die Richtlinie aus den Werkzeugen des Templates in den Kern-Namensräumen plus den Namensräumen, die diese Unterhaltung geladen hat. Bitte um weniger Werkzeuge, um sie einzuschränken, zum Beispiel „nutz nur den Druckerstatus und Benachrichtigungen“; Marshal übergibt dann `tools: [printers.status, notifications.notify]`. Der Bestätigungstext listet die Richtlinie.

Um die Richtlinie zu ändern, öffne die Aufgabe in der Weboberfläche (**Aufgaben**, dann die Aufgabe), wähle **Werkzeugrichtlinie bearbeiten**, hak die Namensräume an, die die Aufgabe nutzen darf, und wähle **Richtlinie speichern**. Das Modell kann eine Richtlinie mit `tasks.update` nicht ändern.

Ein Schritt eines Durchlaufs, den die Richtlinie blockiert hat, zeigt den Fehler `tool.outside_policy` mit einem Link **Werkzeugrichtlinie erweitern** zur Aufgabe. Hintergrund, warum es Richtlinien gibt: [nicht vertrauenswürdige Inhalte](../../concepts/untrusted-content.md#werkzeugrichtlinien-von-aufgaben).

## Budgets und Grenzen

Jede Aufgabe hat ein Budget für eine Aktivierung (Tokens, Kosten, Schritte, Laufzeit, Durchläufe), und Kindaufgaben zehren vom Budget ihrer Eltern. Die Standardwerte und die tägliche Kostengrenze stehen in [`tasks`](../../configuration/marshal-yaml/tasks.md). Eine Aufgabe, die ihr Budget überschreitet, endet mit `failed` und `The task's budget (or its parent's) is used up.` Ist die Tagesgrenze erreicht, startet kein neuer Durchlauf: `Today's cost cap for background tasks is reached.`

Eine Aufgabe aus einem Durchlauf anzulegen oder zu ändern, der nicht vertrauenswürdige Inhalte gelesen hat (Mail, RSS, Webseiten), braucht deine Bestätigung, siehe [Freigaben](approvals.md).

## Prüfen, ob es funktioniert

Öffne **Aufgaben** in der Weboberfläche: Die Aufgabe zeigt ihren Auslöser, den nächsten Termin, den Verbrauch gegen das Budget, die Zustellungen und das letzte Ergebnis. Nutze `tasks.run_now` („führ queue-check jetzt aus“), um eine Zustellung ohne Warten zu testen.

## Alles stoppen

Drück auf der Seite Aufgaben **Alle stoppen** oder sag „stopp alle Hintergrundaufgaben“. Laufende Aufgaben werden abgebrochen, und nichts startet, bis du **Fortsetzen** drückst. Die Aktion wird protokolliert.

## Verwandte Seiten

::: cards
- [Ereignis-Auslöser](event-triggers.md)
- [Tägliches Briefing](daily-briefing.md)
- [Freigaben](approvals.md)
- [Aufgabendefinitionen](../../configuration/tasks-format.md)
- [Einstellungen von `tasks`](../../configuration/marshal-yaml/tasks.md)
- [Aufgaben und Zeitplanung](../../concepts/tasks-and-scheduling.md)
:::
