---
title: Entwicklung
icon: fa-solid fa-screwdriver-wrench
order: 90
description: "Regeln und Projektkonventionen, um Marshal zu ändern: Einrichtung, Tests, Specs, Integrationen, Werkzeuge, Endpunkte, Migrationen und Doku."
---

# Entwicklung

Regeln und Projektkonventionen, um Marshal zu ändern. Die Seiten setzen voraus, dass du .NET, Vue, EF Core und OpenSpec kennst; sie behandeln nur, was für Marshal gilt.

Lies zuerst [`CLAUDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/CLAUDE.md) (unverhandelbare Regeln, Karte des Repositorys, Paketindex) und [`STYLE_GUIDE.md`](https://github.com/lGruenzweilHTL/marshal/blob/master/STYLE_GUIDE.md) (Designphilosophie, Regeln für C#). Beide sind maßgeblich; diese Seiten wiederholen sie nicht.

## Seiten

::: cards
- [Einrichtung](setup.md)
- [Bauen und testen](building-and-testing.md)
- [OpenSpec-Ablauf](openspec-workflow.md)
- [Eine Integration hinzufügen](adding-an-integration.md)
- [Ein Werkzeug hinzufügen](adding-a-tool.md)
- [Weboberfläche](web-frontend.md)
- [Datenbankmigrationen](database-migrations.md)
- [API-Versionierung](api-versioning.md)
- [Release-Ablauf](release-process.md)
- [Stil der Dokumentation](docs-style.md)
- [Diagramme pflegen](../diagrams/README.md)
:::

## Regeln, die jede Änderung prägen

- Keine Implementierung ohne freigegebene OpenSpec-Änderung. Die Specs in `openspec/specs/` gehen vor Code und Doku.
- Jede Integration und jedes Subsystem liegt hinter einer kleinen Schnittstelle.
- Das Modell sieht kein Werkzeug, das es nicht nutzen darf; der Guard setzt dieselben Regeln serverseitig durch.
- Test zuerst. Tests brauchen nie ein echtes LLM.
- Diagramme, Doku, `CLAUDE.md` und die Demo-Simulation ändern sich im selben Commit wie der Code, den sie beschreiben.

## Verwandte Seiten

::: cards
- [Architektur](../concepts/architecture.md)
- [Sicherheitsmodell](../concepts/security-model.md)
:::
