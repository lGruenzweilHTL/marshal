---
title: Marshal-Dokumentation
icon: fa-solid fa-house-signal
description: "Marshal ist ein selbst gehosteter KI-Agent für Home Assistant. Installiere ihn, verbinde deine Dienste, rufe seine API auf und verstehe, wie er dir die Kontrolle lässt."
hero: true
---

Marshal ist ein selbst gehosteter KI-Agent für Home Assistant. Du sprichst mit ihm über einen Assist-Sprachsatelliten, die Home-Assistant-App oder seine eigene Weboberfläche. Er handelt in deinem Zuhause, auf deinem Drucker, in deinem Postfach, deinen Kalendern und Notizen, und er fragt vorher, bevor etwas Riskantes passiert.

![Marshal Home auf dem Desktop: die gruppierte Seitennavigation, zwei offene Freigaben, eine Druckerkarte und laufende Aufgaben](images/dashboard.png)

> [!NOTE]
> Marshal hat noch kein Release. Du baust das Image aus dem Repository, und das Verhalten kann sich noch ohne Ankündigung ändern.

## Hier anfangen

::: cards
- [Überblick](getting-started/overview.md)
- [Schnellstart](getting-started/quick-start.md)
- [Demo-Modus ausprobieren](guides/demo-mode.md)
- [Home Assistant verbinden](getting-started/connect-home-assistant.md)
:::

## Lesepfade

:::: tabs
== Neu dabei
::: steps
1. **Verstehe, was du betreibst:** [Überblick](getting-started/overview.md) und [Voraussetzungen](getting-started/requirements.md).
2. **Installiere es:** [Schnellstart](getting-started/quick-start.md).
3. **Sprich per Stimme mit ihm:** [Home Assistant verbinden](getting-started/connect-home-assistant.md).
4. **Richte den Rest ein:** [nächste Schritte](getting-started/next-steps.md).
5. **Noch kein Home Assistant?** [Probiere den Demo-Modus aus](guides/demo-mode.md).
:::
== Sprachnutzung
::: steps
1. **Füge Marshal einer Pipeline hinzu:** [Assist-Pipeline](guides/voice/assist-pipeline.md).
2. **Stimme jeden Raum ab:** [Satelliten](guides/voice/satellites.md).
3. **Beantworte Rückfragen per Stimme:** [Rückfragen](guides/voice/follow-ups.md).
4. **Installiere die Integration:** [Home-Assistant-Komponente](home-assistant-component/installation.md).
5. **Hör dir deinen Morgen an:** [tägliches Briefing](guides/tasks/daily-briefing.md).
:::
== Integration
::: steps
1. **Sieh dir die Teile an:** [Architektur](concepts/architecture.md).
2. **Ruf die API auf:** [API-Überblick](api/index.md), [Authentifizierung](api/authentication.md) und die [API-Referenz](api/openapi.md).
3. **Gib Clients Tokens:** [Maschinen-Tokens](guides/authentication/machine-tokens.md).
4. **Schick Ereignisse herein:** [Webhooks](api/webhooks.md).
5. **Erweitere die Werkzeuge:** [eigene MCP-Server](guides/integrations/custom-mcp-servers.md) und [MCP-Clients](guides/integrations/mcp-clients.md).
6. **Schlag ein Werkzeug nach:** [Werkzeug-Referenz](reference/tools.md).
:::
== Mitwirkende
::: steps
1. **Lies die Regeln:** [Mitwirken](https://github.com/lGruenzweilHTL/marshal/blob/master/CONTRIBUTING.md).
2. **Richte deinen Rechner ein:** [Entwicklungsumgebung](development/setup.md).
3. **Schlag eine Änderung vor:** [OpenSpec-Ablauf](development/openspec-workflow.md).
4. **Schreib Code:** [eine Integration](development/adding-an-integration.md) oder [ein Werkzeug](development/adding-a-tool.md).
5. **Schreib Dokumentation:** [Dokumentationsstil](development/docs-style.md).
:::
::::

## Die Dokumentation

::: cards
- [Erste Schritte](getting-started/index.md)
- [Installation](installation/index.md)
- [Anleitungen](guides/index.md)
- [Home-Assistant-Komponente](home-assistant-component/index.md)
- [Konfiguration](configuration/index.md)
- [Referenz](reference/index.md)
- [API](api/index.md)
- [Konzepte](concepts/index.md)
- [Entwicklung](development/index.md)
:::

## Gebaut, damit du die Kontrolle behältst

- **Verbotene Werkzeuge sind unsichtbar.** Ein Werkzeug, das das Modell nicht nutzen darf, steht nicht in seiner Werkzeugliste. Siehe [Sichtbarkeit von Werkzeugen](concepts/tool-visibility.md).
- **Riskante Aktionen warten auf dich.** Aktionen der Bestätigungsstufe sind auf dem Server eingefroren, bis du sie freigibst. Siehe [Risiko und Bestätigung](concepts/risk-and-confirmation.md).
- **Mail hat keinen Sendeweg.** Marshal bereitet Entwürfe vor; du sendest sie. Siehe [Mail](guides/integrations/mail.md).
- **Nicht vertrauenswürdige Inhalte bleiben Daten.** Mail, RSS, Notizen und Websuchergebnisse werden nie zu Anweisungen. Siehe [nicht vertrauenswürdige Inhalte](concepts/untrusted-content.md).

## Mehr

::: cards
- [FAQ](faq.md)
- [Roadmap](roadmap.md)
- [Sicherheitsmodell](concepts/security-model.md)
- [Änderungen](https://github.com/lGruenzweilHTL/marshal/blob/master/CHANGELOG.md) {fa-solid fa-list}
  Jede Änderung, Release für Release (auf Englisch).
:::
