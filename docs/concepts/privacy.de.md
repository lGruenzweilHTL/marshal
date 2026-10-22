---
icon: fa-solid fa-eye-slash
description: "Welche Daten deine Marshal-Instanz verlassen, abhängig von den gewählten Modellen und Integrationen, was Marshal speichert und wie lange."
---

# Datenschutz

Diese Seite erklärt, welche Daten deine Marshal-Instanz verlassen, abhängig von den gewählten Modellen und Integrationen, was Marshal speichert und wie lange.

## Das Problem

Ein Hausassistent sieht viel: was du in jedem Raum sagst, wer zu Hause ist, deine Mail, Notizen, deinen Kalender und deine Druckaufträge. Selbst hosten soll das zu Hause halten, aber ein LLM-Agent schickt in jedem Turn Text an ein Modell. Ob dieses Modell auf deiner Hardware oder in einer Cloud läuft, entscheidet den größten Teil deines Datenschutzes. Du musst genau wissen, was wohin geht, um wählen zu können.

## Was die Instanz verlässt, hängt von der Wahl des Modells ab

Marshal spricht über drei Arten von Anbietern mit Modellen:

| Anbieterart | Wo es läuft | Was dein Netz verlässt |
|---|---|---|
| `ollama` | Dein Ollama-Server | Nichts, wenn der Server in deinem Netz steht. |
| `openai-compatible` | Dein llama.cpp-`llama-server`, vLLM oder LM Studio, oder jeder Server mit dieser API | Nichts, wenn der Server in deinem Netz steht. Alles unten Genannte, wenn du ihn auf einen gehosteten Dienst richtest. |
| `anthropic` | Die Claude API von Anthropic | Alles unten Genannte für jeden Aufruf, der an dieses Modell geht. |

Jedes Modell gehört zu einer Stufe, und Marshal wählt Modelle nach Stufe. Du kannst mischen: zum Beispiel ein lokales `fast`-Modell für Sprache und den Prüfer und ein `advisor` in der Cloud. Dann verlassen nur die Aufrufe dein Netz, die an das Cloud-Modell gehen. Siehe [Modelle einrichten](../installation/models-setup.md) und [`models`](../configuration/marshal-yaml/models.md).

### Was ein Aufruf eines Chat-Modells enthält

| Aufruf | Stufe | Gesendeter Inhalt |
|---|---|---|
| Turn einer Unterhaltung | Stufe des Templates, sonst `fast` (Sprache) oder `standard` (Text) | Der vollständige System-Prompt (dein Anzeigename, angeheftetes und abgerufenes Gedächtnis, lokale Zeit und Zeitzone, Bereich und Stockwerk, Notizen zum Satelliten, Zustände der Entitäten in deinem Bereich und der erwähnten Entitäten, Skill-Index), die letzten Wechsel der Sitzung, deine Nachricht, die Werkzeugliste und jedes Werkzeugergebnis des Durchlaufs (zum Beispiel ein Mailtext, eine Notiz, ein Artikel, ein Kalendertermin, der Druckerstatus). |
| Aufgabenlauf | Stufe des Templates | Dasselbe, mit Ziel, Notizen und Nutzdaten des Auslösers der Aufgabe statt des Transkripts. |
| Bestätigungsprüfer | `fast` | Nur die Zusammenfassung der Aktion und deine Antwort. |
| Advisor | `advisor` | Nur die Frage und eine vom Agenten geschriebene Zusammenfassung. Keine Werkzeuge. |
| Zusammenfassung beim Schließen der Sitzung und nächtliche Konsolidierung | günstiges Modell | Deine Turns und die Antworten von Marshal in der Sitzung, plus ein Ausschnitt des bestehenden Gedächtnisses. Nie rohe Werkzeugergebnisse. |
| Subagenten | Stufe des Templates | Der Aufgabentext und die eigenen Werkzeugergebnisse des Subagenten. |
| `printer.inspect`, die Druckaufsicht | ein `vision`-Modell der Stufe des Durchlaufs, sonst `standard` + `vision` | Ein verkleinerter Webcam-Snapshot und die Frage. |
| `artifacts.inspect` | ein `vision`-Modell | Eine gerenderte Seite oder ein Bild des Artefakts und die Frage. |
| Formulierung von Vorschlägen (mit „Marshal beim Lernen helfen“) | `fast` | Die Fakten des Detektors, zum Beispiel der Name des Fensters und die Regenwahrscheinlichkeit. Keine Werkzeuge. |
| Entwurf von Skill-Vorschlägen (mit „Marshal beim Lernen helfen“) | `standard` | Die Benutzeranfragen wiederholter Durchläufe, die Werkzeug- und Argumentnamen und Namen und Beschreibungen bestehender Skills. Nie Werkzeugergebnisse oder Argumentwerte. |

Liegt ein Cloud-Modell in der Stufe `fast`, erreicht jeder Sprach-Turn diesen Anbieter. Sollen Sprache und Zustände deines Zuhauses lokal bleiben, halte `fast` lokal.

### Embeddings

Der Embedding-Anbieter ist `ollama` oder `openai-compatible` (Anthropic bietet keine Embeddings). Er bekommt den Text jedes Turns (mit dem Bereichsnamen), Titel und Inhalt von Gedächtniseinträgen, wenn sie geschrieben werden, Projektbeschreibungen, Korrekturformulierungen und den Text deiner Notizen für den Notizindex. Betreibe ihn lokal, um das zu Hause zu halten. Siehe [`embedding`](../configuration/marshal-yaml/embedding.md).

## Andere Daten, die die Instanz verlassen

| Weg | Ziel | Was |
|---|---|---|
| Integrationen, die du konfigurierst | Home Assistant, Moonraker, dein IMAP-Server, FreshRSS, Seerr / Sonarr / Radarr / Jellyfin, Proxmox, GitHub | Die Anfragen, die jedes Werkzeug braucht. GitHub ist als Einziges davon von Natur aus ein Cloud-Dienst; Marshal liest nur Repositories auf der Allowlist mit rein lesenden Aufrufen. |
| RSS-Volltext | Die Website des Artikels | `rss.fetch_full_text` lädt den in FreshRSS gespeicherten Artikellink. Die Website sieht eine Anfrage deiner Instanz. |
| Websuche | Deine SearXNG-Instanz, dann die Suchmaschinen in `web.searxng.engines` | Der Suchtext von `web.search` und die Frage von `web.answer`, in jedem Durchlauf, auch in Durchläufen, die nicht vertrauenswürdige Inhalte gelesen haben. Marshal öffnet nie eine Ergebnisseite. Siehe [Websuche](../guides/integrations/web-search.md#datenschutz). |
| Push-Benachrichtigungen | `notify`-Dienste von Home Assistant (die Companion-App) | Der `speech`-Text und ein Deep Link. Die Zustellung nach Home Assistant folgt der Einrichtung deiner Companion-App. |
| MCP-Server | Wo der Server läuft | Werkzeugargumente, die das Modell für die Werkzeuge dieses Servers wählt. |
| OpenTelemetry-Export | Dein OTLP-Collector, falls konfiguriert | Spans für Durchläufe, Modell- und Werkzeugaufrufe mit IDs, Quelle, Eingabemodus, Auslöser, Ergebnis, Modell, Werkzeug, Entscheidung des Guards und Token-Zahlen, dazu Metriken. Keine Prompts, Argumente, Ergebnisse oder Antworten, außer du schaltest `telemetry.captureContent` ein: dann Benutzereingabe, Werkzeugargumente, Ergebnisse und Antworten, geschwärzt wie bei der Aufzeichnung der Durchläufe. Nie System-Prompts. Siehe [Observability](../guides/observability.md). |
| Executor-Host | Dein Executor-Host, dann das Coding-Modell | Coding-Sitzungen lassen Claude Code ohne Oberfläche in einer Sandbox laufen, das Repository-Inhalte und die Aufgabe an Anthropic schickt. Nur die auf dem Host eingebundenen Repositories sind erreichbar, und der ausgehende Verkehr der Sandbox ist auf das Netz begrenzt, das du erlaubst. Siehe [Executor-Host](../installation/executor-host.md). |

Die Home-Assistant-Komponente schickt Marshal nur die Felder des Turns (Text, Sprache, Unterhaltungs-ID, Gerät, Satellit, Bereich, Home-Assistant-Benutzer, Eingabemodus). Sie schickt keine Entitätszustände.

## Was Marshal speichert

Alles liegt in `/data` auf deinem Host (siehe [Datenverzeichnis](../reference/data-directory.md)):

| Daten | Wo | Hinweise |
|---|---|---|
| Durchläufe und Schritte | SQLite | Pro Schritt: der vollständige System-Prompt des Kontextaufbaus, Metadaten zu Modell und Tokens, das Reasoning des Modells, Werkzeugargumente, Ergebnisse (standardmäßig bei 8000 Zeichen abgeschnitten) und die Antwort. |
| Sensible Werkzeugergebnisse | SQLite | Als Verweise gespeichert (Nachrichten-ID, Notizpfad) statt als Inhalt. Mail- und Notizwerkzeuge sind standardmäßig sensibel. Das Reasoning von Modellaufrufen nach einem solchen Ergebnis im selben Durchlauf wird nicht gespeichert. Pro Werkzeug änderbar mit `recording.redactTools` und `recording.keepContentTools`. Siehe [`recording`](../configuration/marshal-yaml/recording.md). |
| Sitzungen | SQLite | Quelle, erstellendes Token, Identitätshinweise und wie der Eigentümer bestimmt wurde. |
| Gedächtnis | SQLite | Einträge, Versionen, Kanten, Embeddings, Rückmeldungen zu Projekten. |
| Aufgaben, Benachrichtigungen, Freigaben, Rückfragen | SQLite | Einschließlich des Verlaufs der Benachrichtigungen mit dem Grund für jede Kanalentscheidung. |
| Audit-Log | SQLite | Aktionen außerhalb von Durchläufen: Aktionen in der Oberfläche, Freigaben, Änderungen an Tokens und Secrets, Not-Aus, CLI-Befehle. Werte von Secrets werden nie geschrieben. |
| In der Oberfläche verwaltete Secrets | SQLite | Verschlüsselt mit ASP.NET Core Data Protection; Schlüssel in `/data/keys`. |
| Artefakte | `/data/artifacts/<owner>/` | Hochgeladene Dateien, behaltene Snapshots, gespeicherte Anhänge, Ausgaben des Executors, Diffs und Logs von GitHub. |
| Drucker-Snapshots | `/data` | Werden nach der Aufbewahrung des Snapshot-Speichers entfernt, außer du behältst sie als Artefakte. |
| Notiz-Repository und Index | `/data` | Ein Klon deines Notiz-Repositorys und sein abgeleiteter Suchindex. |
| Sicherungen | `/data/backups` | SQLite-Sicherung, eine Kopie von `/data/keys` und ein NDJSON-Export des Gedächtnisses pro Eigentümer. |
| Vorschläge und Skill-Vorschläge | SQLite | Text und Fakten von Vorschlägen, deine Antworten und die Zähler pro Detektor; entworfene `SKILL.md`-Dateien mit den IDs ihrer Quelldurchläufe. |
| Wartungszustand | SQLite | Pro Eintrag: die Zählerbasis, der zuletzt gelesene Wert und wann er zuletzt erledigt wurde. |

Logs enthalten nie Secrets, Nachrichtentexte, Notizinhalte oder vollständige Prompts.

## Aufbewahrung

| Daten | Aufbewahrt | Einstellung |
|---|---|---|
| Vollständige Nutzdaten der Schritte (Prompts, Werkzeugargumente und -ergebnisse, Antworten) | 30 Tage | `maintenance.payloadRetention` |
| Metadaten von Durchläufen und Schritten (Tokens, Kosten, Werkzeuge, Dauer) | 1 Jahr | `maintenance.metadataRetention` |
| Sicherungen | 7 Kopien | `maintenance.backupCopies` |
| Eval-Fälle | Bis du sie löschst | Von der Bereinigung nicht betroffen. Entstehen nur, wenn du einen Lauf mit eingeschaltetem „Marshal beim Lernen helfen“ bewertest oder ein Admin einen Lauf speichert. Siehe [Evals](../guides/evals.md#datenschutz). |
| Gedächtniseinträge | Bis du sie löschst | Von der Bereinigung nicht betroffen. Löschen ist ein Soft Delete; die Verlaufsansicht zeigt sie weiter. |
| Ausstehende Bestätigungen | 60 Sekunden | `confirmation.ttl` |
| Freigaben im Hintergrund | 10 Minuten | `tasks.approvalTtl` |

Siehe [`maintenance`](../configuration/marshal-yaml/maintenance.md).

> [!NOTE]
> Sicherungen enthalten die Datenbank und die Data-Protection-Schlüssel. Wer eine Sicherung hat, kann deine gespeicherten Secrets entschlüsseln. Bewahre Sicherungen so sorgfältig auf wie die Instanz selbst. Siehe [Sicherungen und Wiederherstellung](../guides/backups-and-restore.md).

## Abwägungen

- **Aufzeichnung ist bewusst an.** Vollständige Nutzdaten machen die Detailansicht eines Laufs nützlich, um zu verstehen und zu debuggen, was Marshal getan hat. Sie sind auch 30 Tage lang eine Aufzeichnung deiner Unterhaltungen. Verkürze `maintenance.payloadRetention`, wenn dir das zu lang ist.
- **Der Kontext ist reichhaltig.** Gedächtnis und Bereichszustände gehen in jeden Turn, um Werkzeugrunden bei Sprache zu sparen. Mit einem Cloud-Modell geht dieser Kontext mit jedem Aufruf mit.
- **Eval-Fälle überdauern die Aufbewahrung.** Ein Fall behält den Text eines Turns, bis du ihn löschst. „Marshal beim Lernen helfen“ ist standardmäßig aus, es gibt also keinen Fall, außer du schaltest es ein oder ein Admin speichert einen Lauf.
- **Schwärzung gilt pro Werkzeug.** Ergebnisse sensibler Werkzeuge werden als Verweise gespeichert, aber das Modell hat den Inhalt während des Durchlaufs gesehen, und der Modellanbieter auch.

## Verwandte Seiten

::: cards
- [Sicherheitsmodell](security-model.md)
- [Datenbesitz](data-ownership.md)
- [Kontextaufbau](context-building.md)
- [Sicherungen und Wiederherstellung](../guides/backups-and-restore.md)
:::
