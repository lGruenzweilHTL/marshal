---
icon: fa-solid fa-binoculars
order: 10
description: "Marshal ist ein selbst gehosteter KI-Agent, mit dem Home Assistant spricht."
---

# Überblick

Marshal ist ein selbst gehosteter KI-Agent, mit dem Home Assistant spricht. Diese Seite erklärt, was du betreibst, womit es sich verbindet und was das Tutorial in diesem Abschnitt aufbaut.

## Was du betreibst

Marshal ist ein Container. Er enthält den ASP.NET-Host (REST-API und Server-Sent Events), die Weboberfläche und eine SQLite-Datenbank. Drei Dinge liegen um ihn herum:

| Teil | Rolle | Nötig |
|---|---|---|
| Marshal-Container | Der Agent, die API und die Weboberfläche | Ja |
| Server für Sprachmodelle | Ollama, ein OpenAI-kompatibler Server oder die Anthropic-API | Ja |
| Home Assistant mit der Marshal-Integration | Leitet Sprach- und Text-Turns von Assist an Marshal weiter | Für Sprache |
| Executor-Host | Eigener Dienst für Coding-Sitzungen | Nein |

Sprache über Home Assistant Assist ist die Hauptschnittstelle. Die Weboberfläche und die öffentliche API sind Nebenschnittstellen.

## Was du konfigurierst

Zwei Volumes enthalten alles:

- `/config` enthält von Hand bearbeitete Dateien wie `marshal.yaml`. Es enthält Namen von Secrets, nie ihre Werte.
- `/data` enthält die Datenbank, Schlüssel für die Verschlüsselung, Backups, Artefakte und das Notizen-Repository.

Siehe [Volumes und Aufbau](../installation/volumes-and-layout.md).

## Was das Tutorial aufbaut

Der [Schnellstart](quick-start.md) baut das Image, startet Marshal mit Ollama, stellt HTTPS davor und legt das erste Konto an. [Home Assistant verbinden](connect-home-assistant.md) fügt danach die Integration hinzu und wählt Marshal in einer Assist-Pipeline aus. [Nächste Schritte](next-steps.md) listet, was du danach einrichtest.

## Verwandte Seiten

::: cards
- [Voraussetzungen](requirements.md)
- [Architektur](../concepts/architecture.md)
:::
