---
icon: fa-solid fa-calendar-days
description: "Wie Marshal arbeitet, wenn niemand mit ihm spricht: dauerhafte Aufgaben, Auslöser, der Worker-Pool, Freigaben, Budgets und wie Ergebnisse dich erreichen."
---

# Aufgaben und Zeitplanung

Diese Seite erklärt, wie Marshal arbeitet, wenn niemand mit ihm spricht: dauerhafte Aufgaben, Auslöser, der Worker-Pool, Freigaben, Budgets und wie Ergebnisse dich erreichen.

## Das Problem

Ein nützlicher Assistent handelt von selbst: ein Morgen-Briefing um 06:00, eine Erinnerung, wenn du beim Baumarkt ankommst, ein Hinweis, wenn ein Druck fertig ist, eine Nachricht zu einer langen Anfrage. Das mit einem LLM zu tun wirft schwierige Fragen auf:

- Was passiert mit einem geplanten Job, wenn der Container neu startet?
- Wie hältst du ein Modell davon ab, sich jede Minute selbst einzuplanen, endlos Kindjobs zu erzeugen oder über Nacht dein Budget auszugeben?
- Wer bestätigt um 03:00 eine riskante Aktion?
- Wie erreicht dich das Ergebnis: gesprochen in der Küche, als Push oder still in einem Eingang?

## Dauerhafte Aufgaben

Eine Aufgabe ist eine Zeile in SQLite, sie übersteht also Neustarts. Sie enthält ihren Eigentümer, die Elternaufgabe, wer sie angelegt hat (`user`, `task` oder `system`), die Art (`agent` oder `system`), Ziel, Template, optional ein Projekt, optional einen Ursprungssatelliten, Auslöser, Status, nächste Laufzeit, Notizen, Budget und Verbrauch, Zustellungen, optional eine Ausnahme von den Ruhezeiten, optional einen Namen (eindeutig pro Eigentümer), ihr letztes Ergebnis und ihre Tiefe.

Der Status durchläuft `pending`, `running`, `waiting`, `done`, `failed` und `cancelled`.

**Systemaufgaben** (Art `system`) führen registrierte C#-Jobs aus, ohne Agentenschleife und ohne Modell-Tokens: die nächtliche Sicherung und den Gedächtnisexport, die Konsolidierung des Gedächtnisses und die Bereinigung nach Aufbewahrungsfristen. Siehe [`maintenance`](../configuration/marshal-yaml/maintenance.md).

## Auslöser

| Auslöser | Feuert |
|---|---|
| `now` | Sofort. |
| `at(time)` | Einmal zu einer Zeit. |
| `cron(expression)` | Nach Zeitplan. |
| `event(filter, once?)` | Wenn ein passendes Ereignis ankommt (eine Beobachtung). |
| `after(task)` | Wenn eine andere Aufgabe mit `done` endet. |

Zeiten von `cron` und `at` werden in der Zeitzone des Eigentümers mit Sommerzeit ausgewertet und in UTC gespeichert, `cron(0 6 * * *)` läuft also auf beiden Seiten einer Zeitumstellung um 06:00 Ortszeit. Nach einem erfolgreichen Durchlauf warten cron-Aufgaben und Beobachtungen ohne `once` auf ihr nächstes Vorkommen; einmalige Beobachtungen und alle anderen Auslöser enden mit `done`.

Der Scheduler ist ein Hintergrunddienst, der bis zum frühesten `next_run_at` schläft.

## Ereignisse und deterministischer Abgleich

Ereignisquellen veröffentlichen normalisierte Ereignisse `{ source, type, entity, payload, ownerId?, time }` auf einem Bus im Prozess:

- Zustandsänderungen in Home Assistant und Push-Aktionen der Companion-App
- Druckereignisse von Moonraker, abgeleitet aus Zustandsübergängen (`started`, `paused`, `resumed`, `complete`, `error`, `cancelled`, Klippy-Shutdown, Fortschrittsmarken, erste Schicht fertig), nie rohe Temperaturänderungen
- neue Mail (IMAP IDLE, mit Polling als Rückfall)
- Callbacks des Executors
- Webhooks von Uptime Kuma, Proxmox und Seerr

Beobachtungen nutzen deklarative Filter (Quelle, Typ, Glob auf die Entität, Bedingungen an die Nutzdaten wie `to` und `from`). Der Abgleich ist deterministisch und passiert, bevor ein Durchlauf startet: Ein Ereignis, das nichts trifft, kostet keinen Modellaufruf. Eine Beobachtung feuert höchstens einmal pro Scharfschaltung. Siehe [Ereignisse](../reference/events.md) und [Ereignis-Auslöser](../guides/tasks/event-triggers.md).

Nutzdaten von Ereignissen sind nicht vertrauenswürdig, außer die Quelle deklariert sich im Code als vertrauenswürdig, die meisten durch Ereignisse gestarteten Durchläufe sind also von Anfang an belastet. Siehe [Nicht vertrauenswürdige Inhalte](untrusted-content.md).

## Der Lebenszyklus eines Durchlaufs

<!--@include: ../_diagrams/task-lifecycle.de.md-->

Innerhalb eines Durchlaufs:

<!--@include: ../_diagrams/task-run.de.md-->

1. **Fällig.** Ein Auslöser feuert, und die Aufgabe wird fällig.
2. **Worker.** Höchstens `tasks.workers` Durchläufe laufen gleichzeitig (standardmäßig 2). Weitere fällige Aufgaben warten in der Reihenfolge von `next_run_at`.
3. **Durchlauf ohne Anfrage.** Die [Agentenschleife](agent-loop.md) läuft als Eigentümer der Aufgabe, mit Template, Projekt und Richtlinie des Ursprungssatelliten der Aufgabe. `denyTools` und `denyConfirm` eines Satelliten folgen seinen Aufgaben und deren Kindaufgaben. Es gelten die Regeln für den Hintergrund: Der Advisor ist sichtbar, und Aufrufe der Bestätigungsstufe gehen in die Freigabe-Warteschlange.
4. **Freigabe.** Ein Aufruf der Bestätigungsstufe friert die Aktion ein (Werkzeug, Argumente, Aufgabe, Eigentümer, Nonce, Gründe), mit einer TTL von 10 Minuten, und benachrichtigt dich per Push mit Aktionen und in der Freigabe-Warteschlange der Weboberfläche. Der Durchlauf endet mit dem Status `waiting`, und sein Worker wird frei. Gibst du frei (Weboberfläche, Push-Aktion oder gesprochene Rückfrage), führt der Server genau die eingefrorene Aktion aus und setzt die Aufgabe mit Entscheidung und Ergebnis fort. Ablehnung und Ablauf setzen sie mit einer Ablehnung fort. Siehe [Freigaben](../guides/tasks/approvals.md).
5. **Aufwecken.** Ein Durchlauf, der mit einem neuen Auslöser endet (zum Beispiel aus `tasks.schedule_wakeup`), wartet. Der nächste Durchlauf bekommt das Ziel, die Notizen der Aufgabe und die Nutzdaten des Auslösers, nicht das vorherige Transkript. Über die Notizen erinnert sich eine Aufgabe zwischen Durchläufen.
6. **Ergebnis und Zustellung.** Die letzte Antwort wird zum Ergebnis: `speech` (kurz, sprechbar) und `body` (Markdown).

## Zustellung

Jede Aufgabe hat eine Liste von Zustellungen, die jeweils unabhängig über den Benachrichtigungs-Router zugestellt werden:

| Kanal | Bekommt |
|---|---|
| `voice` (ein Satellit, ein Bereich oder „wo ich bin“) | `speech`, optional mit einer Rückfrage, damit du antworten kannst |
| `push` | `speech` plus einen Deep Link zum Ergebnis |
| `inbox` | das Ergebnis, für später (zum Beispiel das nächste Briefing) |
| `silent` | nichts wird gesendet; das Ergebnis wird gespeichert |

Eine Zustellung kann auf Bedingungen warten, etwa `state(binary_sensor.kitchen_occupancy = on)`, geprüft, wenn das Ergebnis fertig ist, und bei jeder passenden Zustandsänderung. Läuft vorher `expires` ab, geht sie über einen Ersatzkanal hinaus. Die Priorität einer Zustellung ist `normal` oder `low`; `low` nutzt nie Sprache, und keine Zustellung einer Aufgabe kann `critical` sein.

Der Router wendet dann Anwesenheit, Ruhezeiten, Bündelung und Sprachgrenzen an. Siehe [Benachrichtigungen](../guides/notifications.md).

## Grenzen und der Not-Aus

Das Modell kann Aufgaben anlegen, also setzt der Server Grenzen durch, die das Modell nicht ändern kann:

| Grenze | Standard |
|---|---|
| Budget pro Aufgabe (Tokens, Kosten, Schritte, Laufzeit, Durchläufe); Kindaufgaben zehren vom Budget der Elternaufgabe | Kosten 0,5, 60 Schritte, 30 Minuten, 20 Durchläufe |
| Tägliche Kostengrenze für alle Hintergrund-Durchläufe eines Eigentümers | 2 |
| Rekursionstiefe von Kindaufgaben | 3 |
| Unfertige Kindaufgaben pro Aufgabe (Fan-out) | 5 |
| Kleinstes Weckintervall und kleinste cron-Periode (Templates können eigene setzen) | 5 Minuten |

Ein Durchlauf, der sein Budget überschreitet, stoppt, und die Aufgabe scheitert mit `budget_exceeded`. Die Kosten nutzen dieselbe Preistabelle wie die Kostenansicht. Siehe [`tasks`](../configuration/marshal-yaml/tasks.md).

Der **Not-Aus** bricht alle laufenden und ausstehenden Hintergrundaufgaben des Eigentümers ab und pausiert neue Aufgabenstarts, bis du sie wieder einschaltest. Er ist ein Knopf in der Weboberfläche und das Werkzeug `tasks.stop_all` („stopp alle Hintergrundaufgaben“), und jede Nutzung wird protokolliert.

## Aufgabenwerkzeuge

Das Modell verwaltet Aufgaben mit `tasks.create`, `tasks.schedule_wakeup`, `tasks.watch`, `tasks.update`, `tasks.run_now`, `tasks.list`, `tasks.status`, `tasks.cancel`, `tasks.stop_all` und `tasks.run_executor`. Die schreibenden Werkzeuge sind mit `persists` markiert, sie brauchen in belasteten Durchläufen also eine Bestätigung. Ruhezeiten zu übergehen braucht immer eine Bestätigung. `tasks.update` kann Eigentümer, Budget oder Ursprungssatellit nicht ändern. `tasks.run_executor` ist immer in der Bestätigungsstufe. Siehe [Werkzeugreferenz](../reference/tools.md).

Du kannst Aufgaben auch als Dateien definieren; siehe [Format von Aufgaben](../configuration/tasks-format.md).

## Abwägungen

- **Kein Transkript zwischen Durchläufen.** Ein fortgesetzter Durchlauf sieht nur Ziel, Notizen und Nutzdaten. Das hält Durchläufe klein und verhindert, dass ein belastetes Transkript weitergetragen wird, aber eine Aufgabe muss in ihren Notizen festhalten, was sie später braucht.
- **Anhalten statt warten.** Durchläufe enden, solange eine Freigabe aussteht, eine langsame Entscheidung blockiert also nie einen Worker. Die Aufgabe muss aus den Nutzdaten der Entscheidung wieder anknüpfen.
- **Vorsichtige Grenzen.** Die Standardwerte stoppen ausufernde Kosten früh. Lange Recherche-Aufgaben brauchen womöglich ein größeres Budget in ihrem Template oder ihrer Aufgabendefinition.

## Verwandte Seiten

::: cards
- [Geplante Aufgaben](../guides/tasks/scheduled-tasks.md)
- [Tägliches Briefing](../guides/tasks/daily-briefing.md)
- [Risiko und Bestätigung](risk-and-confirmation.md)
- [Rückfragen](../guides/voice/follow-ups.md)
:::
