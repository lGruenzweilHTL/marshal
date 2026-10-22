---
icon: fa-solid fa-spell-check
description: "Die Begriffe, die die Dokumentation und die Spezifikationen von Marshal nutzen, jeder mit einer Bedeutung."
---

# Glossar

Die Begriffe, die die Dokumentation und die Spezifikationen von Marshal nutzen, jeder mit einer Bedeutung. Andere Seiten nutzen diese Begriffe und keine Synonyme. Der englische Begriff steht in Klammern; die Spezifikationen und der Code nutzen ihn.

## A

**Advisor.** Die stärkste Modellstufe. Der Agent fragt sie über `agent.ask_advisor` um Rat. Sie sieht nur die Frage und eine Zusammenfassung des Kontexts und kann keine Werkzeuge aufrufen. Siehe [Werkzeuge](tools.md#agent-agent).

**Agent.** Der Teil von Marshal, der mit einem Modell spricht, Werkzeuge aufruft und eine Antwort erzeugt. Ein Arbeitsgang ist ein Durchlauf.

**Agentenschleife** (agent loop). Der Zyklus, in dem der Agent Kontext aufbaut, ein Modell aufruft, die vom Modell angeforderten Werkzeugaufrufe ausführt und das wiederholt, bis er eine Antwort hat oder `agent.maxSteps` erreicht. Siehe [Agentenschleife](../concepts/agent-loop.md).

**Agent-Template** (agent template). Eine benannte Konfiguration eines Durchlaufs: Prompt, erlaubte Werkzeuge und Skills, Modellstufe und Grenzen. Ein Subagent führt ein Template aus. Siehe [Agent-Templates](../guides/agent-templates.md).

**AI-Task** (AI task). Eine einmalige Anfrage aus einer Automation in Home Assistant (`ai_task.generate_data`), die Marshal in einem Durchlauf ohne Anfrage mit dem Template `ai_task` als JSON beantwortet. Ihre Sitzung hat die Quelle `automation`. Siehe [AI-Tasks](../home-assistant-component/ai-tasks.md).

**Änderungsvorschlag** (proposal). Eine Änderung, die ein Executor erzeugt hat, etwa ein Branch mit einem Diff. Du prüfst und mergst sie in der Weboberfläche.

**API-Token** (API token). Ein widerrufbares Zugangsdatum mit Scopes für einen Maschinen-Client, gesendet als `Authorization: Bearer`. Marshal speichert nur seinen Hash. Auch Machine-Token genannt. Siehe [CLI](cli.md#marshal-tokens).

**Artefakt** (artifact). Eine Datei, die Marshal für dich speichert: ein Upload, Ausgabe eines Executors, ein behaltener Snapshot, ein Diff oder ein gespeicherter Mailanhang. Jedes hat eine Art, eine Quelle und einen Eigentümer. Siehe [Artefakte](../guides/artifacts.md).

**Assist.** Der Sprachassistent von Home Assistant. Marshal beantwortet Anfragen von Assist über den Konversationsagenten von Marshal. Siehe [Assist-Pipeline](../guides/voice/assist-pipeline.md).

**Aufgabe** (task). Eine dauerhafte Einheit von Hintergrundarbeit mit Ziel, Auslöser, Zustellungen und Status.

**Auslöser** (trigger). Was eine Aufgabe startet: jetzt, eine Zeit, ein cron-Zeitplan, eine andere Aufgabe oder ein Ereignis.

**Ausstehende Aktion** (pending action). Ein Aufruf der Bestätigungsstufe, gespeichert, bis du ihn bestätigst, er abläuft oder abgelöst wird. Eine Sitzung hat höchstens eine.

## B

**Belastung** (taint). Der Zustand eines Durchlaufs, der nicht vertrauenswürdige Inhalte gelesen hat. Sie wird innerhalb des Durchlaufs nie aufgehoben und hebt Werkzeuge mit `persists` in die Bestätigungsstufe.

**Benachrichtigung** (notification). Eine proaktive Nachricht an dich, mit Priorität, Kanal und aufgezeichnetem Ergebnis.

**Beobachtung** (watch). Eine Aufgabe, die ein Ereignis auslöst, das auf einen Filter passt. Sie feuert einmal pro Scharfschaltung. Eine Beobachtung mit `once` endet nach dem ersten Treffer.

**Bestätigungsstufe** (confirm tier). Die Risikostufe, deren Aufrufe erst laufen, nachdem eine Benutzeraktion sie bestätigt hat. Siehe [Risikostufen](risk-tiers.md).

## C

**`critical`.** Die Priorität einer Benachrichtigung, die immer Sprache und Push nutzt. Nur Systemquellen senden sie. Siehe [Prioritäten von Benachrichtigungen](notifications-priorities.md).

## D

**Darstellung** (rendition). Die anzeigbare Form eines Artefakts: ein umgewandeltes Bild, Textseiten oder gerenderte Ansichten eines 3D-Modells.

**Demo-Modus** (demo mode). Simulierte Integrationen anstelle der nicht konfigurierten, plus ein angelegter Beispielverlauf, den `marshal demo reset` entfernt. Siehe [Demo-Modus](../guides/demo-mode.md).

**Display.** Ein Bildschirm im Raum eines Satelliten, auf dem Marshal ein Artefakt zeigen kann.

**Druckaufsicht** (babysitter). Beobachtet einen laufenden Druck über die Webcam und pausiert einen scheiternden Druck. Sie ist eine Systemquelle und kann `critical`-Benachrichtigungen senden.

**Durchlauf** (run). Ein Gang durch die Agentenschleife, von einem Auslöser bis zu einer Antwort oder einem anderen Ergebnis, aufgezeichnet mit seinen Schritten, Kosten und Entscheidungen des Guards. Die Weboberfläche nennt die Liste der Durchläufe **Läufe**.

## E

**Eigentümer** (owner). Der Benutzer, dem eine Zeile gehört. Jede Zeile mit Benutzerdaten hat ein `owner_id`, und jeder Datenzugriff ist auf den Eigentümer beschränkt.

**Eigentümer der Instanz** (instance owner). Der Benutzer, dem die Instanz gehört. Aktionen von Webhooks und der CLI laufen als dieser Eigentümer.

**Eingabeanfrage** (input request). Eine Frage, die ein MCP-Server dem Benutzer während eines Werkzeugaufrufs stellt (MCP-Elicitation, ein Formular oder eine URL). Marshal friert den Aufruf ein, und nur die Weboberfläche antwortet; das Modell sieht die Anfrage nie. Siehe [MCP-Server](../configuration/mcp-servers.md#eingabeanfragen).

**Eingang** (inbox). Deine Liste ungelesener Benachrichtigungen. `notifications.inbox_list` und `notifications.inbox_consume` lesen und leeren sie.

**Eingefrorene Aktion** (frozen action). Werkzeug, Argumente, Nonce und Ablauf einer ausstehenden Aktion, wie sie gespeichert sind. Die Bestätigung führt genau das aus, und das Modell kann es nicht ändern.

**Ereignis** (event). Ein normalisierter Datensatz `{ source, type, entity, payload, ownerId?, time }` auf dem Event-Bus. Siehe [Ereignisse](events.md).

**Ereignisquelle** (event source). Etwas, das Ereignisse veröffentlicht: Home Assistant, Drucker, Mail, Executors oder eine Webhook-Quelle.

**Event-Bus** (event bus). Der Kanal im Prozess, der Ereignisse von Ereignisquellen zum Abgleich der Auslöser trägt.

**Executor.** Ein externer Dienst, der für eine Aufgabe lange Arbeit erledigt, zum Beispiel eine Coding-Sitzung. Er läuft auf dem getrennten [Executor-Host](../installation/executor-host.md) und meldet sich über ein Callback-Token zurück.

## F

**Freigabe** (approval). Die Entscheidung eines Benutzers über eine ausstehende Aktion, die eine Hintergrundaufgabe oder ein anderer nicht interaktiver Durchlauf, etwa ein AI-Task, ausgelöst hat. Freigaben liegen in der Freigabe-Warteschlange und laufen nach `tasks.approvalTtl` ab. Siehe [Freigaben](../guides/tasks/approvals.md).

## G

**Gedächtnis** (memory). Langfristiges Wissen als Gedächtniseinträge. Marshal ruft es per hybrider Suche (Schlüsselwörter und Vektoren) ab und fügt es dem Kontext hinzu.

**Gedächtniseintrag** (chunk). Ein Eintrag im Langzeitgedächtnis, mit Typ, Titel, Inhalt und Wichtigkeit. Ein Eintrag hat eine deterministische ID und einen Versionsverlauf. Siehe [Gedächtnismodell](../concepts/memory-model.md).

**Guard.** Die Prüfung auf dem Server, die für jeden Werkzeugaufruf entscheidet, ob er läuft, eine Bestätigung braucht oder verweigert wird. Sie wiederholt die Regeln, die Werkzeuge verbergen.

## H

**Hintergrundaufgabe** (background task). Arbeit, die der Agent als Aufgabe fortsetzt, ohne dass du wartest. `agent.move_to_background` und die Latenzgrenze machen beide einen Durchlauf zu einer.

## K

**Konsolidierung** (consolidation). Wie das Gedächtnis geschrieben wird: ausdrückliches Merken, Zusammenfassungen beim Schließen einer Sitzung und eine Prüfung jeder Änderung.

**Kontextaufbau** (context builder). Der Teil von Marshal, der den Kontext eines Turns zusammensetzt: den System-Prompt, das Gedächtnis, die Zustände naher Entitäten und die Werkzeugliste. Siehe [Kontextaufbau](../concepts/context-building.md).

## M

**MCP-Server** (MCP server). Ein Model-Context-Protocol-Server, dessen Werkzeuge Marshal dem Modell anbietet. Der MCP-Server von Home Assistant ist einer. Siehe [Eigene MCP-Server](../guides/integrations/custom-mcp-servers.md).

**Modellstufe** (model tier). Eine benannte Klasse von Modellen: `fast`, `standard` oder `advisor`. Marshal leitet jeden Modellaufruf nach Stufe.

## N

**Namensraum** (namespace). Der erste Teil einer Werkzeug-ID, etwa `printer` in `printer.status`. Die Werkzeugsuche lädt Werkzeuge nach Namensraum; Kern-Namensräume sind immer geladen.

**Nicht vertrauenswürdige Ausgabe** (untrusted output). Eine Markierung eines Werkzeugs: Das Ergebnis sind Inhalte von Dritten (Mail, RSS, Notizen, Webseiten). Marshal packt sie als Daten ein, nie als Anweisungen, und belastet den Durchlauf.

**Nonce.** Ein Einmalwert, der mit einer ausstehenden Aktion gespeichert wird. Eine Push-Aktion zählt nur mit der gültigen, nicht abgelaufenen Nonce.

**Not-Aus** (kill switch). `tasks.stop_all`. Stoppt alle Hintergrundaufgaben und pausiert neue, bis du sie in der Weboberfläche fortsetzt.

## P

**`persists`.** Eine Markierung eines Werkzeugs: Das Werkzeug speichert Zustand, auf den der Agent später handelt. In einem belasteten Durchlauf brauchen seine Aufrufe eine Bestätigung.

**Projekt** (project). Ein benannter Arbeitsbereich. Marshal aktiviert Projekte ausdrücklich oder erschlossen und nutzt sie, um passende Gedächtniseinträge zu verstärken.

**Prüfer** (judge). Ein Modellaufruf der Stufe `fast`, der deine Antwort auf eine ausstehende Aktion als `positive`, `neutral` oder `negative` einordnet.

**Push.** Eine Benachrichtigung der Companion-App über Home Assistant.

## R

**Risikostufe** (risk tier). `safe`, `confirm` oder `forbidden`. Siehe [Risikostufen](risk-tiers.md).

**Rückfrage per Sprache** (voice follow-up). Eine gesprochene Frage von Marshal, die auf deine Antwort hört, gestartet von einer Zustellung einer Aufgabe mit `followUp`.

**Ruhezeiten** (quiet hours). Der Wochenplan in deiner Zeitzone, in dem `normal`-Benachrichtigungen zurückgehalten oder still gesendet werden und `low`-Benachrichtigungen in den Eingang gehen.

## S

**Satellit** (satellite). Ein Sprachendpunkt in einem Raum, etwa ein Assist-Satellit. Sein Profil in `satellites.yaml` legt Werkzeugrichtlinie, Display und Lautsprecher fest.

**Schlüsselwort** (keyword). Eine konfigurierte Phrase wie „bestätigt“. Ein genauer Treffer deiner ganzen Antwort bestätigt eine ausstehende Aktion ohne Modellaufruf.

**Schritt** (step). Ein aufgezeichneter Teil eines Durchlaufs: Kontext, ein Modellaufruf, ein Werkzeugaufruf oder die Antwort.

**Scope.** Eine Berechtigung eines API-Tokens, etwa `conversation:write`.

**Secret.** Ein Zugangsdatum. `/config` enthält nur Secret-Namen. Die Werte kommen aus dem Secret-Speicher.

**Sitzung** (session). Eine Unterhaltung mit ihrem Verlauf, zum Beispiel ein Web-Chat oder ein Sprachdialog.

**Skill.** Eine Sammlung von Anweisungen in `SKILL.md`, die der Agent bei Bedarf mit `agent.load_skill` lädt.

**Stufe `forbidden`** (forbidden tier). Die Risikostufe, deren Aufrufe nie laufen. Das Werkzeug ist vor dem Modell verborgen.

**Stufe `safe`** (safe tier). Die Risikostufe, deren Aufrufe sofort laufen.

**Subagent.** Ein verschachtelter Durchlauf, den `agent.delegate` startet. Er führt nie Aktionen der Bestätigungsstufe aus.

## T

**Turn.** Eine Nachricht des Benutzers und die Antwort darauf.

## U

**Überbrückungssatz** (filler). Ein kurzer Satz, den ein langsames eingebautes Werkzeug pro Sprache festlegt. In einem Sprach-Turn spricht Marshal ihn, bevor das Werkzeug läuft, wenn noch nichts gesprochen wurde. Siehe [Werkzeug-Sichtbarkeit](../concepts/tool-visibility.md#gesprochene-überbrückung).

## W

**Warnung zum Zustand** (health warning). Ein Problem, das Marshal über sich selbst meldet, mit Komponente, stabilem Code, Schweregrad und einer von Marshal geschriebenen Meldung. Die Weboberfläche zeigt ein Banner, und `system.health` liefert die Liste. Siehe [Zustandsendpunkte](health-endpoints.md#warnungen-zum-zustand).

**Webhook-Quelle** (webhook source). Eine Ereignisquelle, die HTTP-Posts unter `/api/v1/hooks/<source>` empfängt, etwa `uptime-kuma`, `proxmox` und `seerr`.

**Werkzeug** (tool). Eine Funktion, die das Modell aufrufen kann. Sie hat eine ID `<namespace>.<name>` und einen Modellnamen `<namespace>_<name>`. Siehe [Werkzeuge](tools.md).

**Werkzeug-Pin** (tool pin). Der gespeicherte Fingerabdruck der Definition eines MCP-Werkzeugs. Ein neues oder geändertes Werkzeug bleibt verborgen, bis ein Admin es freigibt. Siehe [Werkzeug-Pinning](../configuration/mcp-servers.md#werkzeug-pinning).

**Werkzeugquelle** (tool source). Ein Anbieter von Werkzeugen, der sich als Einheit registriert und entfernt: die eingebauten Werkzeuge, ein MCP-Server oder ein Skill.

**Werkzeugsuche** (tool retrieval). Dem Modell nur die Werkzeuge der Namensräume anbieten, die eine Sitzung geladen hat, per Embedding-Treffer, Hinweisen oder `agent.find_tools`. Siehe [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md#werkzeugsuche).

## Z

**Zeit bis zum ersten Delta** (time to first delta). Die Zeit von der Anfrage eines Turns bis zum ersten gestreamten Antworttext. Die Ansicht Läufe nennt sie „erste Worte“.

**Zustellung** (delivery). Wohin und wie das Ergebnis einer Aufgabe geht: ein Kanal (`voice`, `push`, `inbox`, `silent`), ein Ziel und Bedingungen.

## Verwandte Seiten

::: cards
- [Werkzeuge](tools.md)
- [Risikostufen](risk-tiers.md)
- [Ereignisse](events.md)
- [Stil der Dokumentation](../development/docs-style.md)
:::
