---
title: Diagramme
icon: fa-solid fa-diagram-project
order: 95
description: "Mermaid-Diagramme der Pakete, des Datenmodells und der wichtigsten Abläufe, jeweils auf der Seite, die sie erklärt."
---

# Diagramme

Mermaid-Diagramme der Pakete, des Datenmodells und der wichtigsten Abläufe, jedes auf der Seite, die es erklärt. Wie du sie aktuell hältst, steht unter [Die Diagramme pflegen](README.md).

::: cards
- [Pakete](../concepts/architecture.md#die-teile) {fa-solid fa-sitemap}
  Projektverweise in src/ und wie Clients und der Executor-Host sich verbinden.
- [Sprach-Turn](../concepts/architecture.md#wie-eine-anfrage-hindurchläuft) {fa-solid fa-microphone-lines}
  Von Home Assistant Assist durch Marshal und zurück.
- [Aktion der Bestätigungsstufe](../concepts/risk-and-confirmation.md#eingefrorene-ausstehende-aktionen) {fa-solid fa-hand}
  Mit Nonce und Ablauf eingefroren, nur durch eine Benutzeraktion bestätigt.
- [Lebenszyklus einer Aufgabe](../concepts/tasks-and-scheduling.md#der-lebenszyklus-eines-durchlaufs) {fa-solid fa-list-check}
  Vom Auslöser über Worker und Freigabe bis zur Zustellung.
- [Datenmodell](erd.md)
:::
