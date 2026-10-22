---
icon: fa-solid fa-map
order: 110
description: "Stell einen Feature-Wunsch. CONTRIBUTING.md beschreibt, wie Änderungen vorgeschlagen werden."
---

# Roadmap

Was Marshal heute kann und welche Ideen zurückgestellt sind. Diese Seite ist eine Zusammenfassung, keine Zusage; die Spezifikationen in `openspec/specs/` legen das genaue Verhalten fest.

## Was es gibt

Alle geplanten Features bis zur dritten Welle der Roadmap sind umgesetzt und archiviert. Danach kamen Chatverlauf, sichtbares Reasoning, Demo-Modus, AI-Tasks für Home Assistant, Pinning von MCP-Werkzeugen mit Eingabeanfragen und Server-Tasks, Werkzeugsuche mit Arbeit an der Sprachlatenz, CalDAV-Kalender, Websuche über SearXNG, Marshal als MCP-Server, MCP Apps, isolierte Extraktion nicht vertrauenswürdiger Inhalte, Gedächtnis mit Gültigkeitszeit und Arbeit in Leerlaufzeiten, Evals und der Schalter fürs Lernen, proaktive Vorschläge und Lernen sowie OpenTelemetry-GenAI-Traces.

| Bereich | Fähigkeiten |
|---|---|
| Sprache und Chat | Assist-Unterhaltungsagent, gestreamte Antworten, gesprochene Überbrückung vor langsamen Werkzeugen, Rückfragen per Sprache, Web-Chat mit Verlauf, Ergebnisse aus dem Hintergrund an Ort und Stelle, Reasoning und Werkzeugschritte |
| Zuhause | Geräte, Bereiche, Timer, Orte und Energiestatistik aus Home Assistant, To-do-Listen über MCP, ortsbezogene Erinnerungen, AI-Tasks für Automationen |
| Drucker | Zustand aus Moonraker, Snapshots, Inspektion, Druckwächter |
| Informationen | Mail (nur lesen und Entwürfe), CalDAV- und Home-Assistant-Kalender, FreshRSS, Notizen in Git, Websuche über SearXNG, tägliches Briefing |
| Medien | Anfragen in Seerr, Sonarr und Radarr nur lesend, Jellyfin-Links, Wiedergabe über Music Assistant |
| Homelab | Ereignisse von Uptime Kuma und Proxmox, Proxmox nur lesend, GitHub nur lesend |
| Artefakte und Displays | Artefaktspeicher und Betrachter, Code-Hervorhebung, 3D-Darstellung, Anzeige auf dem nächsten Display |
| Gedächtnis und Aufgaben | Hybride Gedächtnissuche, Projekte, Konsolidierung, geplante und ereignisgesteuerte Aufgaben, Freigabe-Warteschlange |
| Plattform | Lokale und OIDC-Anmeldung, Maschinen-Tokens, Secret-Store, Skills, Agent-Templates, MCP-Server (Werkzeug-Pinning, Eingabeanfragen, Server-Tasks), Marshal als MCP-Server, MCP Apps, Werkzeugsuche, Warnungen zum Zustand, Aufzeichnung der Durchläufe mit Zeitleisten für Sprache, Evals, OpenTelemetry-GenAI-Traces, Demo-Modus |

Die vollständige Liste steht im [Changelog](https://github.com/lGruenzweilHTL/marshal/blob/master/CHANGELOG.md).

## In Arbeit

Keine Änderung ist in Arbeit. Vorgeschlagene Änderungen liegen in `openspec/changes/`, bis sie archiviert sind.

## Zurückgestellt

Diese Ideen sind für kein bestimmtes Release geplant. Jede braucht ihre eigene OpenSpec-Änderung, bevor die Arbeit beginnt.

| Idee | Warum sie wartet |
|---|---|
| Telefonkanal | Push erreicht dich bei kritischen Warnungen schon. Anrufe brauchen eine SIP-Nummer, Echtzeit-Sprache und einen vertrauenswürdigen Umgang mit der Anrufer-ID. |
| Sprechererkennung | Sie lohnt sich erst mit mehreren Benutzern, und Marshal bekommt heute nie Audio. |
| Browser- oder Computersteuerung | Navigation ist eine vom Aufrufer gewählte URL, jeder Durchlauf wäre also belastet, und das Risiko von Injections läge direkt neben der Steuerung des Zuhauses. |
| Selbstveränderung | Braucht eine Integration eines Git-Hosts und strengen Schutz sensibler Pfade. Marshal würde Änderungen vorschlagen, die etwas außerhalb von Marshal anwendet. |

## Bekannte Lücken

- Mehrere Benutzer auf einer Instanz. Das Datenmodell ist bereit, das Produkt nicht.
- Prüfungen gegen eine echte Installation von Music Assistant stehen noch aus.
- Reasoning wurde noch nicht live gegen ein echtes Reasoning-Modell geprüft, und die Chat-Abläufe des Demo-Modus wurden noch nicht durchgehend mit einem echten Modell geprüft.

## Die Roadmap beeinflussen

Stell einen [Feature-Wunsch](https://github.com/lGruenzweilHTL/marshal/blob/master/.github/ISSUE_TEMPLATE/feature_request.yml). [CONTRIBUTING.md](https://github.com/lGruenzweilHTL/marshal/blob/master/CONTRIBUTING.md) beschreibt, wie Änderungen vorgeschlagen werden.
