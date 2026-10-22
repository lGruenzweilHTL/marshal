---
title: Entwicklung
icon: fa-solid fa-screwdriver-wrench
order: 90
description: "Wie du Marshal änderst: eine Maschine einrichten, bauen und testen, dem OpenSpec-Ablauf folgen und Integrationen, Werkzeuge, Endpunkte und Migrationen hinzufügen."
---

# Entwicklung

Wie du Marshal änderst: eine Maschine einrichten, bauen und testen, dem OpenSpec-Ablauf folgen und Integrationen, Werkzeuge, Endpunkte und Migrationen hinzufügen.

## Wo du anfängst

::: steps
1. **Lies [`CLAUDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/CLAUDE.md)** für die unverhandelbaren Regeln und die Karte des Repositorys. Die Datei ist für KI-Agenten geschrieben und für Menschen genauso nützlich.
2. **Lies [`STYLE_GUIDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md)** für die Designphilosophie und die Regeln für C#.
3. **Lies den [Stil der Dokumentation](docs-style.md)**, bevor du eine Seite in `docs/` anfasst.
4. **Richte deine Maschine ein:** [Einrichtung](setup.md).
5. **Übernimm eine Änderung** über den [OpenSpec-Ablauf](openspec-workflow.md). Marshal nimmt keine Implementierungsarbeit ohne freigegebene Änderung an.
:::

## Seiten

::: cards
- [Einrichtung](setup.md)
  .NET SDK, Node, uv und Docker installieren.
- [Bauen und testen](building-and-testing.md)
  Die Testsuiten für .NET, Web und Home Assistant bauen und ausführen.
- [OpenSpec-Ablauf](openspec-workflow.md)
  Eine Änderung vorschlagen, umsetzen, abgleichen und archivieren.
- [Eine Integration hinzufügen](adding-an-integration.md)
  Ein neues externes System hinter einer kleinen Schnittstelle ergänzen.
- [Ein Werkzeug hinzufügen](adding-a-tool.md)
  Einer bestehenden Quelle ein Werkzeug für das Modell hinzufügen.
- [Weboberfläche](web-frontend.md)
  An der Vue-SPA, ihrem API-Client und ihren Texten arbeiten.
- [Datenbankmigrationen](database-migrations.md)
  Das EF-Core-Modell ändern.
- [API-Versionierung](api-versioning.md)
  Einen öffentlichen API-Endpunkt hinzufügen oder ändern.
- [Release-Prozess](release-process.md)
  Das Image bauen und eine Version veröffentlichen.
:::

## Karte des Repositorys

```tree
Marshal.sln  # Solution
src/  # .NET-Projekte
tests/  # ein Testprojekt pro src-Projekt (Marshal.<Name>.Tests), dazu Marshal.Testing für gemeinsame Fakes
web/  # Vue + Vite + TypeScript SPA
ha/  # Custom-Komponente für Home Assistant (Python, uv-Projekt)
deploy/  # docker-compose.yml und config.example/
docs/  # Dokumentation (dieser Ordner), Diagramme, Bilder
openspec/  # Spezifikationen (specs/) und Änderungen (changes/)
```

Die Projekte in `src/`:

| Projekt | Enthält |
|---|---|
| `Marshal.Api` | ASP.NET-Host: REST- und SSE-Endpunkte, Authentifizierung, liefert die SPA aus. |
| `Marshal.Abstractions` | Kleine gemeinsame Schnittstellen (Anbieter, Kanäle, Executors). |
| `Marshal.Core` | Agentenschleife, Werkzeugregister, Risiko-Engine und Guard, Kontext-Builder, Modell-Router, Sitzungen. |
| `Marshal.Memory` | Gedächtniseinträge, Versionen, Kanten, hybride Suche, Konsolidierung. |
| `Marshal.Tasks` | Dauerhafte Aufgaben, Scheduler, Event-Bus, Auslöser, Zustellungen, Benachrichtigungen, Freigaben, Empfang von Webhooks. |
| `Marshal.Artifacts` | Artefaktspeicher, Erkennung der Art, Darstellungen für Displays, Code-Hervorhebung. |
| `Marshal.Data` | EF-Core-`DbContext`, Migrationen, auf den Eigentümer beschränkte Abfragefilter. |
| `Marshal.Integrations.*` | Ein Projekt pro externem System: HomeAssistant, Moonraker, Mail, FreshRss, Notes, Media, Proxmox, GitHub. |
| `Marshal.ExecutorHost` | Eigener Dienst für die Coding-Sandbox mit eigenem Image. Der Hauptcontainer verweist nie darauf. |

`CLAUDE.md` führt die maßgebliche Karte. Aktualisiere sie, wenn du ein Projekt hinzufügst, umbenennst oder entfernst.

## Regeln, die jede Änderung prägen

- Spezifikationen in `openspec/specs/` gehen vor Code, Notizen und dieser Dokumentation.
- Jede Integration sitzt hinter einer kleinen Schnittstelle.
- Das Modell sieht kein Werkzeug, das es nicht nutzen darf.
- Tests kommen zuerst und brauchen nie ein echtes LLM.
- Diagramme (Mermaid, in den Seiten, die sie erklären) ändern sich im selben Commit wie der Code, den sie beschreiben. Siehe [Die Diagramme pflegen](../diagrams/README.md).

## Verwandte Seiten

::: cards
- [Architektur](../concepts/architecture.md)
- [Sicherheitsmodell](../concepts/security-model.md)
:::
