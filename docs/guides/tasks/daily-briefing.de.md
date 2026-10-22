---
icon: fa-solid fa-mug-hot
description: "Richte das Morgenbriefing aus der Beispielaufgabe, dem Template und den Skills ein, und stimme ab, was es sagt und wo du es hörst."
---

# Tägliches Briefing

Richte das Morgenbriefing aus der Beispielaufgabe, dem Template und den Skills ein, und stimme ab, was es sagt und wo du es hörst.

Das Briefing hat keinen eigenen Code. Es ist eine Aufgabe namens `briefing`, die das Template `briefing` und die Skills `briefing-format` und `rss-digest` nutzt.

## Bevor du anfängst

- Kopiere `deploy/config.example/templates/` und `deploy/config.example/skills/` nach `/config` (mindestens `briefing.md`, `rss-scorer.md`, `briefing-format/` und `rss-digest/`). Beide laden im laufenden Betrieb neu.
- Die Integrationen, die das Briefing liest, sind verbunden: Kalender, [Mail](../integrations/mail.md), [FreshRSS](../integrations/rss-freshrss.md) und [Home Assistant](../integrations/home-assistant.md). Ein Teil ohne Datenquelle fällt weg.
- Deine Sprache ist gesetzt (`owner.language`); das Briefing antwortet in ihr.

## Wie die Teile zusammenpassen

| Teil | Datei | Aufgabe |
|---|---|---|
| Aufgabe | `tasks/briefing.yaml` (Argumente von `tasks.create`) | Wann sie läuft und wohin sie zustellt. |
| Template | `templates/briefing.md` | Allowlist der Werkzeuge, Modellstufe `standard`, `maxSteps: 16`, kein Advisor. Sagt dem Agenten, dass niemand zuhört, damit er keine Fragen stellt. |
| Skill `briefing-format` | `skills/briefing-format/SKILL.md` | Was gesammelt wird und die Form der Antwort. |
| Skill `rss-digest` | `skills/rss-digest/SKILL.md` | Bewertet ungelesene Artikel mit dem Subagenten `rss-scorer`, fasst die besten zusammen und markiert sie als gelesen. |

Das Template erlaubt nur lesende Werkzeuge plus `rss.mark_read`: `calendar.list_events`, `mail.search`, `mail.read_message`, `rss.get_unread`, `rss.fetch_full_text`, `rss.mark_read`, `memory.recall`, das Werkzeug für den Live-Kontext von Home Assistant, `system.health`, `agent.load_skill` und `agent.delegate`. Mails, Feeds und Kalendereinträge sind Material zum Zusammenfassen, nie Anweisungen.

## Schritte

::: steps
1. **Leg die Aufgabe an**

   Frag: „Leg die Aufgabe für das Morgenbriefing mit diesen Einstellungen an“ und füg dann das Beispiel ein.

   ```yaml
   name: briefing
   template: briefing
   goal: >-
     Prepare today's morning briefing: appointments, mail that needs attention and waiting drafts, the RSS digest,
     the weather and the house. Follow the briefing-format skill.
   trigger:
     kind: cron
     expression: "0 6 * * *"
   deliver:
     - channel: voice
       followUp: true
       when:
         - state($person = home)
         - state(binary_sensor.kitchen_occupancy = on)
       expires: "11:00"
       fallback: push
     - channel: inbox
   ```

   Ändere `binary_sensor.kitchen_occupancy` in einen Belegungssensor deines Raums. `$person` löst zu deiner Personen-Entität auf (Benachrichtigungseinstellungen).

2. **Versteh die Zustellungen**

   Um 03:00 sammelt ein Vorbereitungsdurchlauf die Termine und den RSS-Digest (`prepare.before: 3h`). Um 06:00 wird das Briefing mit diesen Notizen vorbereitet und prüft Mail, Wetter und Haus frisch. Die gesprochene Fassung spielt, wenn beide Bedingungen gelten, zum Beispiel wenn du um 07:30 in die Küche kommst, und der Satellit hört danach auf „erzähl mir mehr“. Passiert beides bis 11:00 nicht, wird der gesprochene Text stattdessen per Push an dein Handy geschickt. Die Zustellung `inbox` behält die geschriebene Fassung mit Links in der Weboberfläche.

3. **Kenne das Antwortformat**

   Der Skill `briefing-format` lässt den Agenten in zwei Teilen antworten, getrennt durch eine Leerzeile:

   - Erster Absatz: die gesprochene Fassung (die `speech` des Aufgabenergebnisses). Ein Absatz aus einfachen Sätzen, 60 bis 90 Sekunden (etwa 150 bis 220 Wörter), das Wichtigste zuerst, am Ende ein Angebot für mehr Details.
   - Alles danach: die geschriebene Fassung (der `body`) in Markdown mit den Abschnitten Today, Mail, Feeds, House and weather und Marshal, unter 600 Wörtern.

   Der Teil Marshal listet die eigenen [Warnungen zum Zustand](../../reference/health-endpoints.md#warnungen-zum-zustand) von Marshal, etwa ein MCP-Werkzeug, das auf deine Freigabe wartet, oder eine Integration, die ausgefallen ist. `system.health` ist nur sichtbar, solange es Warnungen gibt, ein Briefing an einem gesunden Tag hat also keinen Teil Marshal.

4. **Frag bei Bedarf danach**

   Sag „guten Morgen“. Der Skill sagt dem Agenten, in der Unterhaltung kein neues Briefing zu bauen: Er ruft `tasks_status` mit `briefing` auf. Ist das Ergebnis von heute und jünger als zwei Stunden, liest er die vorbereitete gesprochene Fassung vor. Sonst ruft er `tasks_run_now` auf, die Aufgabe bereitet das Briefing im Hintergrund vor und stellt es über ihre Zustellungen zu.
:::

## Abstimmen

- Andere Uhrzeit: Ändere den Auslöser mit `tasks.update`, zum Beispiel `expression: "30 6 * * 1-5"`.
- Anderer Inhalt: Bearbeite `briefing-format/SKILL.md` (Änderungen greifen ohne Neustart). Eine Quelle fügst du hinzu, indem du ihre Werkzeuge in die Liste `tools` des Templates und einen Schritt in den Skill aufnimmst.
- Günstiger: Setz `modelTier` des Templates auf `fast`, verringere `maxSteps` oder lass den RSS-Schritt im Skill weg.
- Budget: Das Briefing läuft mit dem Standardbudget für Aufgaben; siehe [`tasks`](../../configuration/marshal-yaml/tasks.md).

## Prüfen, ob es funktioniert

Führe `tasks_run_now briefing` aus („führ das Briefing jetzt aus“), öffne dann **Aufgaben** und das letzte Ergebnis der Aufgabe, und **Läufe**, um jeden Werkzeugaufruf zu sehen. Dieselbe Pipeline ist in der [Anleitung zu Skills](../skills.md) und unter [Agent-Templates](../agent-templates.md) beschrieben.

## Verwandte Seiten

::: cards
- [Geplante Aufgaben](scheduled-tasks.md)
- [Skills](../skills.md)
- [Agent-Templates](../agent-templates.md)
- [Benachrichtigungen](../notifications.md)
- [Aufgabendefinitionen](../../configuration/tasks-format.md)
:::
