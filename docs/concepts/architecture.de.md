---
icon: fa-solid fa-sitemap
description: "Wie Marshal aufgebaut ist: welche Prozesse laufen, wie die Projekte voneinander abhängen und warum die Grenzen dort liegen, wo sie liegen."
---

# Architektur

Diese Seite erklärt, wie Marshal aufgebaut ist: welche Prozesse laufen, wie die Projekte voneinander abhängen und warum die Grenzen dort liegen, wo sie liegen.

## Das Problem

Ein Hausassistent, der mit einem LLM spricht, berührt viele Dinge gleichzeitig: Home Assistant, Drucker, Mail, Feeds, Notizen, Medienserver, Infrastruktur und eine Coding-Sandbox. Jedes davon hat sein eigenes Protokoll, seine eigenen Fehlerbilder und Risiken. Spricht der Agentencode direkt mit ihnen, gehen drei Dinge schief:

- Eine kaputte Integration legt den ganzen Agenten lahm.
- Jede Integration braucht eigene Sicherheitsprüfungen, und manche werden vergessen.
- Tests brauchen echte Dienste und ein echtes LLM, sie sind also langsam und unzuverlässig.

Marshal antwortet darauf mit einem zentralen Harness, durch das jede Anfrage läuft, und kleinen Schnittstellen zwischen dem Harness und allem anderen.

## Die Teile

<!--@include: ../_diagrams/packages.md-->

Marshal läuft als ein ASP.NET-Container plus einem optionalen, getrennten Executor-Host:

| Teil | Rolle |
|---|---|
| `Marshal.Api` | Der Host. Bietet die versionierte REST-API mit SSE-Streaming, Authentifizierung, Webhooks und die Weboberfläche. Es ist das einzige Projekt, das SDKs von Anbietern referenziert (Ollama, Anthropic, OpenAI-kompatibel). |
| `Marshal.Abstractions` | Kleine gemeinsame Schnittstellen (Zugriff auf `IChatClient`, Werkzeuge, Kanäle, Executors, `Result`). Feature-Projekte hängen davon ab, nicht voneinander. |
| `Marshal.Core` | Das Agenten-Harness: Agentenschleife, Werkzeug-Registry, Sichtbarkeitsfilter, Risk Engine und Guard, Kontextaufbau, Model Router, Sitzungen, Bestätigungen, MCP-Client, Skills und Templates. |
| `Marshal.Memory` | Gedächtniseinträge, hybride Suche, Projektaktivierung und Konsolidierung. |
| `Marshal.Tasks` | Dauerhafte Aufgaben, Scheduler und Worker-Pool, Event-Bus und Abgleich der Auslöser, Zustellungen, Benachrichtigungen, Freigabe-Warteschlange, Rückfragen, Executors, Systemjobs und Webhook-Empfang. |
| `Marshal.Integrations.*` | Ein Projekt pro Integration (Home Assistant, Moonraker, Mail, FreshRSS, Notizen, Medien, Proxmox, GitHub). Jedes steuert Werkzeuge, Ereignisquellen oder Kontext hinter den gemeinsamen Schnittstellen bei. |
| `Marshal.Artifacts` | Der Artefaktspeicher und seine Darstellungen. |
| `Marshal.Demo` | Demo-Modus: simuliertes Home Assistant, Drucker, Mail, RSS und Notizen hinter denselben Schnittstellen, dazu das Anlegen der Beispieldaten. Nur `Marshal.Api` referenziert es, und es kontaktiert nie einen Netzwerk-Host. Siehe [Demo-Modus](../guides/demo-mode.md). |
| `Marshal.Data` | EF Core auf SQLite, Migrationen, Abfragefilter pro Eigentümer. |
| `Marshal.ExecutorHost` | Ein getrennter Dienst für Coding-Sitzungen. Er hat sein eigenes Image und Deployment und wird von Marshal nie referenziert. Siehe [Executor-Host](../installation/executor-host.md). |
| `web/` | Die Vue-Single-Page-App, gebaut in das `wwwroot` des Hosts. |
| `ha/` | Die Custom-Komponente für Home Assistant, die Marshal zu einem Konversationsagenten für Assist macht. |

Clients erreichen Marshal auf drei Wegen: Home Assistant Assist über die Custom-Komponente (die Hauptschnittstelle), die Weboberfläche und die öffentliche API. Alle drei nutzen denselben Turn-Endpunkt, `POST /api/v1/conversation/turns` (siehe [Konversations-API](../api/conversation.md)).

<!--@include: ../_diagrams/packages-runtime.md-->

## Wie eine Anfrage hindurchläuft

<!--@include: ../_diagrams/voice-turn-request.de.md-->

In `Marshal.Core` führt die Agentenschleife den Turn aus:

<!--@include: ../_diagrams/voice-turn-agent-loop.de.md-->

1. Assist übergibt den transkribierten Text an die Marshal-Komponente, die ihn mit Satellit, Bereich, Sprache und Eingabemodus weiterleitet. Sie schickt keine Entitätszustände.
2. `Marshal.Api` authentifiziert das Maschinen-Token, prüft den Scope `conversation:write` und sucht die Sitzung oder legt sie an.
3. `Marshal.Core` prüft auf eine ausstehende Bestätigung und führt dann die [Agentenschleife](agent-loop.md) aus: Kontext aufbauen, das Modell mit den sichtbaren Werkzeugen aufrufen, jeden Aufruf prüfen, ausführen, wiederholen.
4. Werkzeuge erreichen Home Assistant (über seinen MCP-Server und die REST- und Websocket-APIs), Drucker, Mail und andere Integrationen.
5. Die Antwort streamt als SSE-Ereignisse `delta` zurück und endet mit einem letzten Ereignis, das `speech` und `continueConversation` trägt. Die Weboberfläche bekommt für denselben Turn auch Ereignisse `reasoning` und `tool`; Token-Clients wie Home Assistant nie.

Arbeit im Hintergrund (geplante Aufgaben, Beobachtungen von Ereignissen, Freigaben) nutzt dieselbe Schleife und denselben Guard, ohne HTTP-Anfrage. Siehe [Aufgaben und Zeitplanung](tasks-and-scheduling.md).

## Designregeln hinter den Grenzen

**Überall kleine Schnittstellen.** Jede Integration und jedes Subsystem sitzt hinter einer kleinen Schnittstelle, auch mit genau einer Implementierung: `IChatClient`, `IEmbeddingGenerator`, `IMailProvider`, `INotificationChannel`, `IExecutor`, `ISecretStore`, `IRiskTargetResolver`, `IContextContributor`, `IRunRecorder`. Tests ersetzen sie durch handgeschriebene Fakes, kein Test braucht also ein echtes LLM oder einen echten Dienst.

**Neutral gegenüber Anbietern.** Alle Chat-Modelle laufen über `IChatClient` und alle Embedding-Modelle über `IEmbeddingGenerator`. Es gibt drei Arten von Anbietern: `ollama`, `openai-compatible` und `anthropic`. Nur `Marshal.Api` referenziert die SDKs der Anbieter, das Harness hängt also nie von einem Hersteller ab. Modelle werden nach Stufe (`fast`, `standard`, `advisor`) und Fähigkeit (`vision`, `tools`, `contextLength`) gewählt, nicht nach Namen. Siehe [`models`](../configuration/marshal-yaml/models.md).

**Ein Harness, ein Guard.** Eingebaute Werkzeuge, MCP-Werkzeuge und Skills laufen alle durch dieselbe Registry, denselben Sichtbarkeitsfilter und denselben Guard. Eine Integration kann sie nicht umgehen.

**Isolierte Fehler.** Ein MCP-Server, der nicht startet, oder eine Werkzeugquelle, die mit einem reservierten Namensraum kollidiert, wird allein abgewiesen; alles andere funktioniert weiter. Ein Werkzeug, das eine Ausnahme wirft, gibt dem Modell ein Fehlerergebnis, statt den Turn zu beenden. Scheitert die Suche im Gedächtnis, läuft der Turn ohne Gedächtnis weiter, die Gerätesteuerung hängt also nie vom Embedding-Server ab.

**Konfiguration als Dateien, Secrets über Namen.** `/config` enthält YAML und Markdown, das du in git halten kannst. Es enthält nie Werte von Secrets, nur Secret-Namen, die `ISecretStore` auflöst. Siehe [Sicherheitsmodell](security-model.md).

**Versionierte API im Code.** API-Versionen werden einmal im Code festgelegt und nie in der Konfiguration, eine Konfigurationsänderung kann also den Vertrag, den ein Client sieht, nicht ändern.

## Abwägungen

- **Ein Prozess, Event-Bus im Prozess.** Der Event-Bus ist ein Kanal im Prozess, und Aufgaben liegen in SQLite. Das hält das Deployment bei einem Container und übersteht Neustarts, aber Marshal skaliert nicht horizontal.
- **SQLite.** Eine Datei, leicht zu sichern, mit FTS5 und `sqlite-vec` für die Suche. Die Nebenläufigkeit ist begrenzter als bei einer Server-Datenbank, was für einen Haushalt akzeptabel ist.
- **Eigene Agentenschleife.** Marshal lässt den Modell-Client Werkzeuge nicht automatisch aufrufen. Die Schleife ist schlank und gehört Marshal, jeder Aufruf geht also durch den Guard und wird aufgezeichnet. Der Preis ist, dass Marshal seine eigene Schleife pflegt, statt die eines Frameworks zu nutzen.
- **Getrennter Executor-Host.** Coding-Sitzungen brauchen Docker. Liegen sie auf einem anderen Host, braucht der Hauptcontainer nie einen Docker-Socket, auf Kosten eines zweiten Deployments.

## Verwandte Seiten

::: cards
- [Agentenschleife](agent-loop.md)
- [Sicherheitsmodell](security-model.md)
- [Volumes und Aufbau](../installation/volumes-and-layout.md)
- [Überblick zur Entwicklung](../development/index.md)
:::
