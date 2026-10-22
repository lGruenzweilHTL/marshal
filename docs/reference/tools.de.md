---
icon: fa-solid fa-toolbox
description: "Jedes Werkzeug, das das Modell bekommen kann, nach Integration gruppiert, mit Risikostufe, Argumenten, Ergebnis und den Bedingungen, unter denen es verborgen ist."
---

# Werkzeuge

Jedes Werkzeug, das das Modell bekommen kann, nach Integration gruppiert, mit Risikostufe, Argumenten, Ergebnis und den Bedingungen, unter denen es verborgen ist.

## Konventionen

- **Werkzeug-ID.** Jedes Werkzeug hat eine interne ID `<namespace>.<name>`. Risikoregeln, Richtlinien der Satelliten, Allowlists der Templates, Datensätze der Durchläufe und das Audit-Log nutzen sie. Das Modell sieht den Namen `<namespace>_<name>` (zum Beispiel ist `mail.search` für das Modell `mail_search`). Diese Seite listet die ID.
- **Stufe.** Die Standard-[Risikostufe](risk-tiers.md) des Werkzeugs. Eine Regel in `risk.yaml` kann sie heben oder senken, und ein belasteter Durchlauf hebt Werkzeuge mit `persists` von `safe` auf `confirm`.
- **Untrusted.** Das Ergebnis ist Inhalt von Dritten. Marshal verpackt es als nicht vertrauenswürdige Daten, und der Durchlauf wird mit der angegebenen Quelle belastet. Siehe [nicht vertrauenswürdige Inhalte](../concepts/untrusted-content.md).
- **Persists.** Das Werkzeug speichert Zustand, auf den der Agent später reagiert. In einem belasteten Durchlauf brauchen seine Aufrufe eine Bestätigung.
- **Verborgen, wenn.** Marshal entfernt das Werkzeug vor jedem Modellaufruf aus der Werkzeugliste. Der Guard setzt dieselben Regeln bei jedem Aufruf durch. Neben den Bedingungen unten ist ein Werkzeug auch verborgen, wenn eine Risikoregel es als `forbidden` markiert, wenn das aktive [Agent-Template](../guides/agent-templates.md) es nicht erlaubt, wenn `denyTools` eines Satelliten darauf passt, wenn seine Stufe auf einem Satelliten, dessen `denyConfirm` darauf passt, `confirm` ergibt, und bei Werkzeugen der Bestätigungsstufe in Subagenten. Siehe [Werkzeug-Sichtbarkeit](../concepts/tool-visibility.md).
- Argumente mit `?` sind optional. Die Werkzeuge `tasks.*`, die Werkzeuge `printer.*` und `rss.fetch_full_text` lehnen unbekannte Argumente ab.

## Übersicht

| Namensraum | Werkzeuge | Quelle |
|---|---|---|
| `agent` | `ask_advisor`, `move_to_background`, `load_skill`, `delegate` | Eingebaut |
| `memory` | `remember`, `recall`, `correct_project` | Eingebaut |
| `tasks` | `create`, `schedule_wakeup`, `watch`, `update`, `run_now`, `list`, `status`, `cancel`, `stop_all`, `run_executor` | Eingebaut |
| `notifications` | `notify`, `inbox_list`, `inbox_consume` | Eingebaut |
| `timers` | `start`, `cancel`, `status` | Eingebaut (Home Assistant) |
| `places` | `find`, `create_here` | Eingebaut (Home Assistant) |
| `energy` | `usage` | Eingebaut (Home Assistant) |
| `calendar` | `list_calendars`, `list_events`, `find_free_time`, `create_event`, `update_event` | Eingebaut (Home Assistant, CalDAV) |
| `music` | `search`, `play`, `queue`, `control`, `transfer`, `group`, `ungroup` | Eingebaut (Music Assistant über Home Assistant) |
| `printer` | `status`, `list_files`, `job_queue`, `history`, `snapshot`, `inspect`, `pause`, `emergency_stop`, `start_print`, `resume`, `cancel`, `set_temperature`, `home`, `move`, `edit_queue`, `run_macro` | Eingebaut (Moonraker) |
| `mail` | `search`, `read_message`, `read_thread`, `list_folders`, `list_labels`, `create_draft`, `update_draft`, `save_attachment` | Eingebaut |
| `rss` | `list_feeds`, `list_categories`, `get_unread`, `get_article`, `search`, `mark_read`, `fetch_full_text` | Eingebaut (FreshRSS) |
| `notes` | `list`, `read`, `search` | Eingebaut |
| `web` | `search`, `answer` | Eingebaut (SearXNG) |
| `media` | `search`, `request`, `requests`, `upcoming`, `downloads` | Eingebaut (Seerr, Sonarr, Radarr) |
| `infra` | `overview`, `guests`, `storage`, `tasks` | Eingebaut (Proxmox VE) |
| `repos` | `prs`, `pr`, `issues`, `issue`, `runs`, `run_log` | Eingebaut (GitHub) |
| `artifacts` | `list`, `show`, `inspect` | Eingebaut |
| `system` | `health` | Eingebaut |
| `maintenance` | `status`, `done` | Eingebaut |
| `ha` und andere | Was der Server meldet | [MCP-Server](#werkzeuge-von-mcp-servern) |

Die Namensräume `agent`, `memory`, `tasks`, `notifications`, `timers`, `mail`, `rss`, `printer`, `calendar`, `notes`, `places`, `energy`, `artifacts`, `infra`, `repos`, `media`, `music`, `system`, `maintenance` und `web` sind für eingebaute Werkzeuge reserviert. Ein MCP-Server mit einer dieser IDs wird abgelehnt.

Marshal hat kein Werkzeug, das Mail sendet, keines, das rohen G-Code sendet, und keines, das Mails, Dateien oder Kalendertermine löscht. Diese Fähigkeiten existieren nicht als Codepfade.

## Agent (`agent`)

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `agent.ask_advisor` | :badge[safe]{tip} | `question`, `context` | Die Textantwort eines Aufrufs ohne Werkzeuge an die Modellstufe `advisor`. Der Advisor sieht nur die Frage und deine Zusammenfassung des Kontexts. | Der Turn ist ein Sprach-Turn; kein `advisor`-Modell ist konfiguriert; die Grenze an Aufrufen pro Durchlauf ist erreicht (`agent.advisor.maxCallsPerRun` oder `maxAdvisorCalls` des Templates); die tägliche Kostengrenze des Eigentümers für den Advisor ist erreicht. |
| `agent.move_to_background` | :badge[safe]{tip} | `statusMessage` | Beendet den Turn mit der Statusnachricht und setzt den Durchlauf als Hintergrundaufgabe fort. Der Benutzer bekommt die endgültige Antwort als Benachrichtigung. | Der Turn ist nicht interaktiv oder ein Turn eines Subagenten, oder Hintergrundarbeit ist nicht verfügbar. |
| `agent.load_skill` | :badge[safe]{tip} | `name`, `file?` | Den Text des Skills mit der Liste seiner Dateien oder eine Datei des Skills. Die Ausgabe ist vertrauenswürdig. | Das aktive Template erlaubt keinen Skill, oder es gibt keinen Skill. |
| `agent.find_tools` | :badge[safe]{tip} | `query` | Lädt die Namensräume mit sichtbaren Werkzeugen, die am besten zur Anfrage passen (ein in der Anfrage genannter Namensraum zuerst), und gibt ihre Namen und Beschreibungen zurück. Ihre Werkzeuge werden ab dem nächsten Modellaufruf angeboten. | Die Werkzeugsuche ist abgeschaltet oder für den Durchlauf übersprungen, oder jeder Namensraum mit sichtbaren Werkzeugen ist schon geladen. |
| `agent.delegate` | :badge[safe]{tip} | `template`, `task` | Den Schlusstext eines Subagenten, der das genannte [Agent-Template](../guides/agent-templates.md) ausführt. Ein belasteter Aufrufer bekommt den Text als nicht vertrauenswürdige Daten verpackt. Der Subagent führt nie Aktionen der Bestätigungsstufe aus. | Der Durchlauf ist an der Grenze der Delegationstiefe (`agent.maxDelegationDepth`), oder es gibt kein Template außer dem Standard. |

## Gedächtnis (`memory`)

Die Ausgabe des Gedächtnisses ist vertrauenswürdig: Nur du oder ein bestätigter Schreibvorgang bringt Inhalte ins Gedächtnis.

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `memory.remember` | :badge[safe]{tip}, persists | `type`, `title`, `content`, `importance?`, `scope?`, `project?`, `sourceRef?`, `validFrom?`, `validTo?` | Speichert einen Eintrag. Gleicher Typ und gleicher Titel aktualisieren den bestehenden Eintrag. `type` ist `fact`, `preference`, `event`, `procedure`, `person` oder `device`; `importance` ist 1 bis 5 (Standard 3); `scope` ist `general` oder `project`; `validFrom` und `validTo` sind Daten oder ISO-8601-Zeitpunkte in der Zeitzone des Eigentümers (Standard: gültig ab jetzt, offen). Das Werkzeug kann keinen Eintrag anheften. | Nie. |
| `memory.recall` | :badge[safe]{tip} | `query`, `type?`, `project?`, `limit?`, `asOf?` | Einträge, die zur Anfrage passen, einschließlich angehefteter Einträge. `limit` ist 1 bis 30 (Standard 10). Ohne `asOf` nur Einträge, die jetzt gelten; mit `asOf` (Datum oder Zeitpunkt), was damals galt, einschließlich geschlossener Einträge und früherer Versionen, jeweils mit ihrer Gültigkeitszeit. | Nie. |
| `memory.correct_project` | :badge[safe]{tip} | `kind`, `project?` | Zeichnet deine Korrektur des erschlossenen Projekts auf. `kind` ist `rejected`, `switched` (braucht `project`) oder `confirmed`. | Es gibt kein Projekt. |

## Aufgaben (`tasks`)

Alle Werkzeuge für Aufgaben sind sicher. Werkzeuge, die Arbeit für später speichern, sind `persists`, und ein Argument `overrideQuietHoursUntil` hebt den Aufruf immer auf `confirm` mit dem Grund `quiet_hours_override`. Werkzeuge, die Arbeit starten, sind verborgen, solange der Not-Aus aktiv ist. Innerhalb eines Durchlaufs einer Aufgabe ist eine neue Aufgabe eine Unteraufgabe.

`trigger` ist ein Objekt `{ kind, time?, expression?, task? }`. `kind` ist `now`, `at` (mit lokaler Zeit `time`), `cron` (mit einem fünfteiligen Ausdruck `expression` in lokaler Zeit) oder `after` (mit ID oder Name einer anderen Aufgabe `task`). `deliver` ist eine Liste von Zustellungen: `channel` (`voice`, `push`, `inbox`, `silent`), `target?` (nur Sprache: Entitäts-ID eines Satelliten), `priority?` (`normal` oder `low`), `followUp?`, `when?` (Bedingungen), `expires?` (lokale Frist) und `fallback?`.

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `tasks.create` | :badge[safe]{tip}, persists | `goal`, `trigger?`, `name?`, `template?`, `notes?`, `tools?`, `prepare?`, `deliver?`, `overrideQuietHoursUntil?` | Die angelegte Aufgabe als Zusammenfassung. | Not-Aus aktiv; innerhalb eines Durchlaufs einer Aufgabe ist die Grenze der Aufgabentiefe (`tasks.maxDepth`) oder der Unteraufgaben (`tasks.maxActiveChildren`) erreicht. |
| `tasks.schedule_wakeup` | :badge[safe]{tip}, persists | `at?` oder `inMinutes?`, `notes?` | Die Weckzeit. Beendet den aktuellen Durchlauf und führt die Aufgabe später mit ihrem Ziel und den neuen Notizen erneut aus. | Der Aufruf ist nicht in einem Durchlauf einer Hintergrundaufgabe, oder der Not-Aus ist aktiv. |
| `tasks.watch` | :badge[safe]{tip}, persists | `filter`, `goal`, `name?`, `once?`, `deliver?`, `overrideQuietHoursUntil?` | Die angelegte, durch Ereignisse ausgelöste Aufgabe. `filter` braucht `source` und kann `type`, einen Glob für `entity` und Bedingungen an die Nutzdaten wie `to` und `from` tragen. | Wie bei `tasks.create`. |
| `tasks.update` | :badge[safe]{tip}, persists | `task`, `goal?`, `trigger?`, `deliver?`, `template?`, `name?`, `prepare?`, `overrideQuietHoursUntil?` | Die geänderte Aufgabe. | Es gibt keine Aufgabe. |
| `tasks.run_now` | :badge[safe]{tip} | `task` | `{ started, task }`. Scheitert mit `tasks.running`, solange die Aufgabe läuft. | Not-Aus aktiv. |
| `tasks.list` | :badge[safe]{tip} | `status?` | Bis zu 50 Aufgaben. `status` ist `active`, `pending`, `running`, `waiting`, `done`, `failed` oder `cancelled`. | Nie. |
| `tasks.status` | :badge[safe]{tip} | `task` | Den Status der Aufgabe und ihr letztes Ergebnis mit seinem Alter. | Nie. |
| `tasks.cancel` | :badge[safe]{tip} | `task` | Die Zahl der abgebrochenen Aufgaben. Bricht auch nicht beendete Unteraufgaben ab. | Nie. |
| `tasks.stop_all` | :badge[safe]{tip} | keine | Not-Aus: bricht alle Hintergrundaufgaben ab und pausiert neue, bis du sie in der Weboberfläche fortsetzt. | Nie. |
| `tasks.run_executor` | :badge[confirm]{warning}, persists | `executor`, `spec`, `onDoneGoal`, `deliver?` | Startet lange externe Arbeit in einem [Executor](../installation/executor-host.md) und führt `onDoneGoal` aus, wenn sie fertig ist. `executor` ist einer der konfigurierten Executor-Namen (derzeit `claude_code`). | Kein Executor ist konfiguriert; Not-Aus aktiv; innerhalb eines Durchlaufs einer Aufgabe ist die Grenze der Tiefe oder der Unteraufgaben erreicht. |

## Benachrichtigungen (`notifications`)

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `notifications.notify` | :badge[safe]{tip} | `message`, `priority?`, `deliver?` | `{ sent: true }`. `priority` ist `normal` (Standard) oder `low`; `critical` scheitert an der Validierung. `deliver` ist `{ channel, target? }` mit `channel` `voice`, `push` oder `inbox`; nur Sprache hat ein Ziel. `low` nutzt nie Sprache. | Nie. |
| `notifications.inbox_list` | :badge[safe]{tip}, untrusted (`notifications`) | keine | Bis zu 50 ungelesene Einträge des Posteingangs, neueste zuerst, mit ID, Quelle, Priorität, Text und Zeit. | Nie. |
| `notifications.inbox_consume` | :badge[safe]{tip} | `ids` | `{ consumed }`. Markiert die Einträge als erledigt. | Nie. |

Siehe [Prioritäten von Benachrichtigungen](notifications-priorities.md).

## Werkzeuge für Home Assistant

Die Steuerung von Home-Assistant-Entitäten kommt vom MCP-Server von HA, siehe [Werkzeuge von MCP-Servern](#werkzeuge-von-mcp-servern). Die eingebauten Werkzeuge unten nutzen die REST- und die Websocket-API von HA.

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `timers.start` | :badge[safe]{tip} | `duration`, `name?` | Die gesprochene Antwort von HA. `duration` nimmt `10m`, `1h30m`, `90s` oder ISO 8601 (`PT1H30M`) an. Läuft auf dem Gerät, mit dem der Benutzer spricht. | Der Turn hat kein Gerät, oder der MCP-Server von HA bietet schon `HassStartTimer` an. |
| `timers.cancel` | :badge[safe]{tip} | `name?` | Die gesprochene Antwort von HA. Bricht nach Name ab oder den einzigen Timer. | Wie bei `timers.start`. |
| `timers.status` | :badge[safe]{tip} | keine | Die Timer, die auf dem Gerät laufen. | Wie bei `timers.start`. |
| `places.find` | :badge[safe]{tip} | `query?` | HA-Zonen mit Radius, ob du drin bist, und `personState`, den Wert, den deine Personen-Entität dort annimmt. | Keine `ha.url` konfiguriert. Scheitert mit `ha.unavailable`, solange HA nicht verbunden ist. |
| `places.create_here` | :badge[confirm]{warning} | `name`, `radius?` | Legt eine Zone an der aktuellen Position deiner Personen-Entität an. `radius` ist 50 bis 1000 m (Standard 150). | Keine `ha.url`, oder für den Eigentümer ist keine Personen-Entität gesetzt. |
| `energy.usage` | :badge[safe]{tip} | `period`, `from?`, `to?`, `compareWith?` | Netzbezug und Einspeisung, Solar, Batterie, Verbrauch des Haushalts, Kosten und die Geräte mit dem höchsten Verbrauch. `period` ist `today`, `yesterday`, `week`, `month`, `last_week`, `last_month` oder `range` (mit `from` und `to`). `compareWith: previous` ergänzt den vorherigen Zeitraum. | Keine `ha.url`, oder das Energie-Dashboard von HA hat keine Quellen. |

## Kalender (`calendar`)

Kalender kommen aus Kalender-Entitäten von Home Assistant (`calendar.*`) und aus CalDAV-Konten (`caldav:<account>/<calendar>`). Zeiten sind lokal (`2026-09-30T09:00`) oder ein Datum (`2026-09-30`) für ganze Tage. Einrichtung: [Deine Kalender nutzen](../guides/integrations/calendar.md).

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `calendar.list_calendars` | :badge[safe]{tip} | keine | Kalender mit `id`, `name`, `canCreate`, `canUpdate`. | Kein Kalender ist konfiguriert. |
| `calendar.list_events` | :badge[safe]{tip}, untrusted (`calendar`) | `calendar?`, `start`, `end` | Termine im Zeitraum, älteste zuerst, aus allen Kalendern oder einem. Wiederkehrende Termine kommen als Vorkommen mit `recurrenceId`, `recurring`, `seriesId` und `originalStart`. CalDAV-Termine ergänzen `organizer` und `attendees` (`name`, `address`, `role`, `status`: `needs-action`, `accepted`, `declined`, `tentative`, `delegated`). | Kein Kalender ist konfiguriert. |
| `calendar.find_free_time` | :badge[safe]{tip} | `start`, `end`, `duration` (Minuten), `calendars?`, `within?` (`08:00-18:00`) | Bis zu 20 freie Zeitfenster von mindestens `duration`, früheste zuerst, und die gelesenen Kalender. Als frei markierte Termine und Besprechungen, die das Konto abgelehnt hat, gelten nicht als belegt. Nur Zeiten und Kalender-IDs, daher nicht untrusted. | Kein Kalender ist konfiguriert. |
| `calendar.create_event` | :badge[confirm]{warning} | `calendar`, `summary`, `start`, `end`, `description?`, `location?` | Legt den Termin an. | Kein Kalender kann Termine anlegen. |
| `calendar.update_event` | :badge[confirm]{warning} | `calendar`, `eventId`, `summary`, `start`, `end`, `recurrenceId?`, `scope?`, `description?`, `location?` | Ersetzt den Termin. Gib jedes Feld an. `scope: occurrence` (Standard mit `recurrenceId`) ändert ein Vorkommen, `scope: series` alle Vorkommen. `scope` scheitert mit `calendar.invalid_arguments` bei Kalendern, die keine einzelnen Vorkommen ändern können (Home Assistant). | Kein Kalender kann Termine ändern. |

## Musik (`music`)

Alle Musikwerkzeuge sind sicher und scheitern mit `ha.unreachable`, wenn HA nicht erreichbar ist, und mit `music.failed`, wenn HA oder Music Assistant den Aufruf ablehnt. Werkzeuge mit `speaker` nutzen den Lautsprecher des fragenden Satelliten, wenn du das Argument weglässt. Nur `music.search` und `music.queue` geben Text von Dritten zurück, daher sind nur sie untrusted (Quelle der Belastung `music`).

| Werkzeug-ID | Argumente | Ergebnis |
|---|---|---|
| `music.search` | `query`, `type?` | Je bis zu 10 Titel, Alben, Künstler, Playlists und Radiosender, mit Name, Künstlern, Album und `uri`. `type` ist `track`, `album`, `artist`, `playlist` oder `radio`. Untrusted. |
| `music.play` | `item`, `type?`, `speaker?`, `enqueue?`, `radio?` | Spielt eine Suchphrase oder eine `uri` von Music Assistant ab, nie eine Web-URL. `enqueue` ist `play` (Standard), `replace`, `next`, `replace_next` oder `add`. `radio: true` setzt mit ähnlicher Musik fort. |
| `music.queue` | `speaker?` | Wiedergabestatus, aktueller und nächster Eintrag, Länge der Warteschlange, Zufallswiedergabe und Wiederholung. Untrusted. |
| `music.control` | `action`, `speaker?`, `volume?` | `action` ist `pause`, `resume`, `next`, `previous`, `stop` oder `volume`. `volume` ist 0 bis 100. |
| `music.transfer` | `to`, `from?` | Verschiebt Warteschlange und Wiedergabe auf einen anderen Lautsprecher. |
| `music.group` | `speakers` | Verbindet mindestens zwei Lautsprecher zu einer synchronen Gruppe. Der erste Lautsprecher führt. |
| `music.ungroup` | `speaker` | Nimmt einen Lautsprecher aus seiner Gruppe. |

Verborgen, wenn `ha.url` nicht gesetzt oder `music.speakers` leer ist. `music.play`, `music.queue`, `music.control` und `music.transfer` nutzen standardmäßig den Lautsprecher des fragenden Raums; ein Turn ohne Lautsprecher am Satelliten muss einen nennen (`music.no_speaker`).

## Drucker (`printer`)

Jedes Druckerwerkzeug nimmt ein optionales Argument `printer`. Standard ist der einzige konfigurierte Drucker; bei mehreren Druckern und ohne Wert scheitert der Aufruf und nennt die Namen. Alle Druckerwerkzeuge sind verborgen, wenn kein Drucker konfiguriert ist.

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `printer.status` | :badge[safe]{tip} | `printer?` | Zustand, Druckauftrag, Fortschritt, Schicht, Restzeit und Temperaturen, dazu der Zustand des Druckwächters. | Kein Drucker. |
| `printer.list_files` | :badge[safe]{tip} | `printer?` | Bis zu 50 druckbare Dateien, neueste zuerst. | Kein Drucker. |
| `printer.job_queue` | :badge[safe]{tip} | `printer?` | Die Warteschlange der Druckaufträge. | Kein Drucker. |
| `printer.history` | :badge[safe]{tip} | `printer?`, `limit?` | Letzte Druckaufträge, neueste zuerst. `limit` ist 1 bis 50 (Standard 10). | Kein Drucker. |
| `printer.snapshot` | :badge[safe]{tip} | `printer?` | Einen Webcam-Snapshot als Markdown-Bild mit einer URL vom selben Ursprung. | Kein Drucker hat eine Webcam. |
| `printer.inspect` | :badge[safe]{tip} | `printer?`, `question` | Die Antwort eines Vision-Modells zum Webcam-Bild. | Kein Vision-Modell in der Stufe des Durchlaufs, oder keine Webcam. |
| `printer.pause` | :badge[safe]{tip} | `printer?` | Pausiert den laufenden Druck. | Kein Drucker. |
| `printer.emergency_stop` | :badge[safe]{tip} | `printer?` | Not-Halt. Klipper braucht danach einen Neustart. | Kein Drucker. |
| `printer.start_print` | :badge[confirm]{warning} | `printer?`, `file` | Startet den Druck einer Datei aus `printer.list_files`. | Kein Drucker. |
| `printer.resume` | :badge[confirm]{warning} | `printer?` | Setzt einen pausierten Druck fort. | Kein Drucker. |
| `printer.cancel` | :badge[confirm]{warning} | `printer?` | Bricht den laufenden Druck ab. | Kein Drucker. |
| `printer.set_temperature` | :badge[confirm]{warning} | `printer?`, `heater`, `target` | Setzt ein Heizziel in Grad Celsius (0 schaltet aus). `heater` ist `extruder` oder `bed`. Wird vor jeder Bestätigung gegen die Grenzen des Druckers geprüft. | Kein Drucker. |
| `printer.home` | :badge[confirm]{warning} | `printer?`, `axes?` | Referenziert die angegebenen Achsen (`x`, `y`, `z`) oder alle. | Kein Drucker. |
| `printer.move` | :badge[confirm]{warning} | `printer?`, `axis`, `distance` | Bewegt eine Achse relativ zu ihrer Position, in mm. | Kein Drucker. |
| `printer.edit_queue` | :badge[confirm]{warning} | `printer?`, `action`, `files?`, `job_ids?`, `job_id?` | `action` ist `add`, `remove`, `clear`, `pause`, `start` oder `jump`. | Kein Drucker. |
| `printer.run_macro` | :badge[confirm]{warning} | `printer?`, `name` | Führt ein freigegebenes Klipper-Makro ohne Argumente aus. `name` ist auf die konfigurierten Makros beschränkt. | Kein Drucker hat ein freigegebenes Makro. |

`printer.pause` und `printer.emergency_stop` sind absichtlich sicher: Das Anhalten eines missratenen Drucks darf nicht auf eine Bestätigung warten. Roher G-Code, Neustarts von Firmware oder Host und das Löschen von Dateien gibt es nicht als Werkzeuge.

## Mail (`mail`)

Mailwerkzeuge senden, löschen oder verschieben nie Mails. Alle Mailwerkzeuge sind verborgen, wenn es kein Mailkonto gibt. `account` ist der Alias des Kontos; ohne ihn sucht eine Suche in allen Konten.

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `mail.search` | :badge[safe]{tip}, untrusted (`mail`), sensitive | `account?`, `from?`, `to?`, `subject?`, `text?`, `since?`, `before?`, `unread?`, `folder?`, `limit?` | Zusammenfassungen der Nachrichten mit Alias des Kontos und Nachrichten-ID. `since` und `before` sind `YYYY-MM-DD`. `folder` ist standardmäßig der Posteingang. `limit` ist 1 bis 50 (Standard 20). | Kein Mailkonto. |
| `mail.read_message` | :badge[safe]{tip}, untrusted (`mail`), sensitive | `id` | Eine Nachricht als Text, Anhänge nach Namen gelistet. | Kein Mailkonto. |
| `mail.read_thread` | :badge[safe]{tip}, untrusted (`mail`), sensitive | `id` | Die ganze Unterhaltung, älteste zuerst. | Kein Mailkonto. |
| `mail.list_folders` | :badge[safe]{tip} | `account?` | Die Ordner des Kontos. | Kein Mailkonto. |
| `mail.list_labels` | :badge[safe]{tip} | `account?` | Die Labels des Kontos. | Kein Konto unterstützt Labels. |
| `mail.create_draft` | :badge[safe]{tip} | `account?`, `to`, `subject`, `body`, `replyTo?` | Speichert einen Entwurf im Entwurfsordner des Kontos. Nichts wird gesendet. | Kein Mailkonto. |
| `mail.update_draft` | :badge[safe]{tip} | `id`, `to`, `subject`, `body`, `replyTo?` | Ersetzt einen Entwurf, den Marshal angelegt hat. Nichts wird gesendet. | Kein Mailkonto. |
| `mail.save_attachment` | :badge[confirm]{warning}, persists, sensitive | `message`, `attachment` | Speichert einen Anhang als belastetes Artefakt (Quelle `mail`) und gibt seine ID zurück. `attachment` ist sein Name oder seine Position (1 ist der erste). | Kein Mailkonto. |

## RSS (`rss`)

RSS-Werkzeuge lesen und markieren Artikel. Es gibt kein Werkzeug zum Abonnieren oder Löschen. Alle sind verborgen, wenn FreshRSS nicht konfiguriert ist. `rss.fetch_full_text` nimmt eine Artikel-ID, nie eine URL.

| Werkzeug-ID | Stufe | Argumente | Ergebnis |
|---|---|---|---|
| `rss.list_feeds` | :badge[safe]{tip} | keine | Feeds mit Titel, Kategorien und URL der Website. |
| `rss.list_categories` | :badge[safe]{tip} | keine | Namen der Kategorien. |
| `rss.get_unread` | :badge[safe]{tip}, untrusted (`rss`) | `category?`, `since?`, `limit?` | Ungelesene Artikel, neueste zuerst, mit einer Zusammenfassung, die nach 300 Zeichen abgeschnitten ist. `since` ist `YYYY-MM-DD` oder ISO 8601. `limit` ist 1 bis 50 (Standard 20). |
| `rss.get_article` | :badge[safe]{tip}, untrusted (`rss`) | `id` | Den Feed-Inhalt eines Artikels. |
| `rss.search` | :badge[safe]{tip}, untrusted (`rss`) | `query`, `limit?` | Neuere Artikel (gelesen und ungelesen), deren Titel oder Zusammenfassung die Wörter enthält. |
| `rss.mark_read` | :badge[safe]{tip} | `ids` | Markiert Artikel als gelesen, ohne Bestätigung. |
| `rss.fetch_full_text` | :badge[safe]{tip}, untrusted (`rss`) | `articleId` | Lesbaren Volltext aus der Webseite des Artikels, nach einer Längengrenze abgeschnitten. |

## Notizen (`notes`)

Alle Notizwerkzeuge sind sicher, untrusted (Quelle der Belastung `notes`) und sensitive. Sie sind verborgen, wenn keine Notizquelle konfiguriert ist. Marshal schreibt nie Notizen.

| Werkzeug-ID | Argumente | Ergebnis |
|---|---|---|
| `notes.list` | `folder?` | Bis zu 500 Notizen mit Pfad, Größe und Änderungszeit. |
| `notes.read` | `path` | Eine Notiz. |
| `notes.search` | `query`, `limit?` | Passende Abschnitte, gefunden nach Bedeutung und Schlüsselwörtern. `limit` ist 1 bis 20. |

## Websuche (`web`)

Webwerkzeuge sind in jedem Durchlauf sicher, auch in einem belasteten. Sie sind verborgen, wenn `web.searxng` nicht konfiguriert ist. Marshal öffnet nie eine Ergebnis-URL. Siehe [Websuche](../guides/integrations/web-search.md).

| Werkzeug-ID | Stufe | Argumente | Ergebnis |
|---|---|---|---|
| `web.search` | :badge[safe]{tip}, untrusted (`web`) | `query`, `limit?` | Bis zu `limit` Ergebnisse (Standard und Maximum `web.searxng.maxResults`) mit Ergebnis-ID, Titel, Ausschnitt, URL und Domain. `query` hat höchstens 300 Zeichen. Ergebnisse bleiben eine Stunde lang als `web:<result id>` ansprechbar. |
| `web.answer` | :badge[safe]{tip} | `question`, `answerType`, `values?` | `{ answer, domains }`. Nur Titel und Ausschnitte erreichen den isolierten Extraktor. Typisierte Antworten belasten nicht; eine `text`-Antwort belastet mit `web` und wird als nicht vertrauenswürdige Daten verpackt. `values` ist für `enum` Pflicht. |

## Medien (`media`)

Alle Medienwerkzeuge sind untrusted (Quelle der Belastung `media`) und verborgen, wenn `media.seerr.url` nicht gesetzt ist. Marshal liest Sonarr und Radarr nur und legt Anfragen in Seerr an; es kann sie weder löschen noch ändern.

| Werkzeug-ID | Stufe | Argumente | Ergebnis |
|---|---|---|---|
| `media.search` | :badge[safe]{tip} | `query`, `type?` | Bis zu 10 Filme und Serien mit `tmdbId`, Typ, Titel, Jahr, kurzer Beschreibung, Zahl der Staffeln, Verfügbarkeit und einem Jellyfin-Link. `type` ist `movie` oder `show`. |
| `media.request` | :badge[confirm]{warning} | `type`, `tmdbId`, `title`, `year`, `seasons?` | Legt eine Anfrage in Seerr an. Nach der Bestätigung prüft Marshal, dass die ID noch zu diesem Titel und Jahr gehört, und scheitert sonst mit `media.title_mismatch`. Scheitert mit `media.already_available` oder `media.already_requested`. Eine Serie ohne `seasons` fragt alle Staffeln an. |
| `media.requests` | :badge[safe]{tip} | `status?` | Bis zu 20 neueste Anfragen. `status` ist `pending`, `approved`, `declined`, `failed` oder `completed`. |
| `media.upcoming` | :badge[safe]{tip} | `days?` | Kommende Folgen (Sonarr) und Filmstarts (Radarr), höchstens 50. `days` ist 1 bis 30 (Standard 7). |
| `media.downloads` | :badge[safe]{tip} | keine | Die Download-Warteschlangen von Sonarr und Radarr. |

## Infrastruktur (`infra`)

Alle Infrastrukturwerkzeuge sind sicher und vertrauenswürdig. Sie senden nur `GET`-Anfragen an Proxmox VE und scheitern mit `infra.unavailable`, wenn die API nicht erreichbar ist oder das Token ablehnt. Sie sind verborgen, wenn `proxmox.url` nicht gesetzt ist.

| Werkzeug-ID | Argumente | Ergebnis |
|---|---|---|
| `infra.overview` | keine | Nodes mit CPU, Arbeitsspeicher und Uptime, Zahl der Gäste nach Status und Speicher, die zu mehr als 80 % voll sind. |
| `infra.guests` | `node?`, `status?` | VMs und Container mit ID, Name, Node, Status, CPU, Arbeitsspeicher und Uptime. `status` ist `running`, `stopped` oder `paused`. |
| `infra.storage` | keine | Speicher mit Typ, belegter und gesamter Größe in GB und belegtem Anteil in Prozent. Geteilte Speicher erscheinen einmal. |
| `infra.tasks` | `failedOnly?`, `since?` | Die neuesten 50 Proxmox-Tasks (Backups, Migrationen, Starts) mit Typ, Gast-ID, Node, Benutzer, Zeiten und Status. `since` ist eine ISO-8601-Zeit. |

## GitHub-Repositories (`repos`)

Alle Repository-Werkzeuge sind sicher und untrusted (Quelle der Belastung `github`). Jedes Werkzeug nimmt ein Pflichtargument `repo`, ein Enum der konfigurierten Repository-IDs. Sie sind verborgen, wenn unter `github.repos` kein Repository konfiguriert ist. Ein Wert außerhalb der Allowlist scheitert mit `repos.invalid_arguments`, und keine Anfrage erreicht GitHub.

| Werkzeug-ID | Argumente | Ergebnis |
|---|---|---|
| `repos.prs` | `repo`, `state?` | Bis zu 30 neueste Pull Requests. `state` ist `open` (Standard), `closed` oder `all`. |
| `repos.pr` | `repo`, `number` | Titel, Autor, Status, Branches, Text und Zahl der Änderungen. Der Diff wird als `diff`-Artefakt aufbewahrt (`diffArtifactId`). |
| `repos.issues` | `repo`, `state?`, `query?` | Bis zu 30 neueste Issues ohne Pull Requests. `query` filtert die Titel unter den 100 neuesten. |
| `repos.issue` | `repo`, `number` | Titel, Autor, Status, Labels, Text und die 20 neuesten Kommentare. |
| `repos.runs` | `repo`, `branch?` | Bis zu 20 neueste Workflow-Läufe. |
| `repos.run_log` | `repo`, `runId` | Für jeden gescheiterten Job seine gescheiterten Schritte und die letzten 50 Log-Zeilen. Das vollständige Log wird als `code`-Artefakt aufbewahrt (`logArtifactId`). |

## Artefakte (`artifacts`)

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `artifacts.list` | :badge[safe]{tip}, untrusted (`artifact`) | `kind?`, `source?`, `query?` | Bis zu 50 neueste Artefakte mit ID, Name, Art, Quelle, Größe und Erstellungszeit. `kind` ist `image`, `svg`, `video`, `model3d`, `code`, `diff`, `pdf`, `markdown`, `html` oder `other`; `source` ist `upload`, `executor`, `printer`, `mail` oder `github`. | Nie. |
| `artifacts.show` | :badge[safe]{tip} | `id`, `page?` | Zeigt das Artefakt in einem Web-Turn im Chat. In einem Turn eines Satelliten spielt es seine Darstellung auf dem Display im Raum ab (`page` wählt die Seite, Standard 1). Das Modell kann kein anderes Display wählen. | Der Turn ist kein Web-Turn, und der Satellit hat kein Display. |
| `artifacts.inspect` | :badge[safe]{tip}, untrusted (`artifact`) | `id`, `question`, `page?` | Die Antwort eines Vision-Modells zu einer Seite der Darstellung und die Zahl der Seiten. Scheitert mit `artifacts.no_rendition`, wenn die Darstellung kein Bild ist. | Weder die Stufe des Durchlaufs noch `standard` hat ein Vision-Modell. |

## System (`system`)

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `system.health` | :badge[safe]{tip} | keine | Die aktuellen [Warnungen zum Zustand](health-endpoints.md#warnungen-zum-zustand): Komponente, Code, Schwere, die Meldung in der Sprache des Durchlaufs und seit wann. Vertrauenswürdige Ausgabe: Die Meldungen stammen von Marshal selbst. | Es gibt keine Warnungen. |

## Wartung (`maintenance`)

| Werkzeug-ID | Stufe | Argumente | Ergebnis | Verborgen, wenn |
|---|---|---|---|---|
| `maintenance.status` | :badge[safe]{tip} | `item?` (ID des Eintrags; weglassen für alle) | Pro Eintrag: Name, Nutzung, Intervall, Einheit, Zustand (`ok`, `soon`, `due`) und wann er zuletzt erledigt wurde. | Keine `maintenance.items` konfiguriert. |
| `maintenance.done` | :badge[safe]{tip}, persists | `item` | Den Eintrag, nachdem er als erledigt markiert ist: Nutzung `0`, Zustand `ok`. | Keine `maintenance.items` konfiguriert. |

Siehe [Wartung nach Nutzung verfolgen](../guides/maintenance.md).

## Werkzeuge von MCP-Servern

Werkzeuge von [MCP-Servern](../configuration/mcp-servers.md) nutzen die Server-ID (oder `toolPrefix`) als Namensraum, ohne Präfix `mcp.`. Namen bleiben, wie der Server sie meldet; Zeichen außerhalb von `[a-zA-Z0-9_-]` werden durch `_` ersetzt. Ein Name für das Modell mit mehr als 64 Zeichen wird gekürzt und endet auf einen Hash. Zum Beispiel meldet der MCP-Server von Home Assistant (ID `ha`) `intent__HassTurnOn`; daraus werden die ID `ha.intent__HassTurnOn` und der Modellname `ha_intent__HassTurnOn`.

- **Stufe.** Externe Werkzeuge stehen standardmäßig auf `confirm`. Ändere das mit Regeln in `risk.yaml`, zum Beispiel `{ tool: "ha.*" } -> safe`. Die Beispiel-`risk.yaml` lässt Schlösser, Alarmzentralen sowie Garagentore, Türen und Tore auf `confirm`.
- **Untrusted.** Werkzeuge eines Servers mit `untrusted: true` und Werkzeuge, die auf `untrustedTools` passen, belasten den Durchlauf mit der Quelle `mcp:<server id>`. Andere Werkzeuge nicht.
- **Verborgen, wenn.** Der Server ist ausgefallen, die Erkennung ist gescheitert oder er wurde abgelehnt. Die Werkzeuge erscheinen wieder, wenn Marshal sich neu verbindet. `/health` zeigt eine Warnung, solange ein Server ausgefallen ist. Ein Werkzeug, das nach der ersten Erkennung des Servers neu ist oder sich geändert hat, bleibt verborgen, bis ein Admin es freigibt ([Werkzeug-Pinning](../configuration/mcp-servers.md#werkzeug-pinning)).
- **Eingaben und Server-Tasks.** Eine Eingabeanfrage eines Servers erreicht nie das Modell; das Werkzeugergebnis sagt, dass der Benutzer in der Weboberfläche gefragt wurde. Ein Aufruf, den der Server als Server-Task fortsetzt, gibt ein Ergebnis zurück, das sagt, dass die Arbeit im Hintergrund weiterläuft; das Ergebnis kommt später über eine wartende Aufgabe. Siehe [Eingabeanfragen](../configuration/mcp-servers.md#eingabeanfragen) und [Server-Tasks](../configuration/mcp-servers.md#server-tasks).
- **Home Assistant.** `ha.*`-Werkzeuge lösen ihre Ziele (Name, Bereich, Etage, Domain, Geräteklasse) zu konkreten Entitäten auf, Regeln können also auf `domain` und `deviceClass` passen. Ein Aufruf, dessen Ziele sich nicht auflösen lassen, ist `confirm` mit dem Grund `unresolved_target`. Der Kontext-Builder gibt dem Modell die Zustände von Entitäten in der Nähe; für mehr ruft das Modell `ha.homeassistant__GetLiveContext` auf.
- **Demo-Modus.** Ist Home Assistant simuliert und kein MCP-Server `ha` konfiguriert, bietet der [Demo-Modus](../guides/demo-mode.md) selbst `ha.intent__HassTurnOn`, `ha.intent__HassTurnOff`, `ha.intent__HassLightSet`, `ha.intent__HassClimateSetTemperature` und `ha.homeassistant__GetLiveContext` an, gestützt auf die simulierten Zustände. Sie stehen wie die Werkzeuge des Servers standardmäßig auf `confirm`, es gelten also dieselben Regeln in `risk.yaml`.

## Verwandte Seiten

::: cards
- [Risikostufen](risk-tiers.md)
- [Werkzeug-Sichtbarkeit](../concepts/tool-visibility.md)
- [Ein Werkzeug hinzufügen](../development/adding-a-tool.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
:::
