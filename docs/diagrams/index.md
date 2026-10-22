---
title: Diagrams
icon: fa-solid fa-diagram-project
order: 95
description: "Mermaid diagrams of the packages, the data model and the main flows, each on the page that explains it."
---

# Diagrams

Mermaid diagrams of the packages, the data model and the main flows. Each diagram is written in the Markdown of the page that explains it, and Clay draws it when the site is built. How to keep them current is in [maintaining the diagrams](README.md).

::: cards
- [Packages](../concepts/architecture.md#the-parts) {fa-solid fa-sitemap}
  Projects in src/, web/ and ha/, their references and key packages.
- [Voice turn](../concepts/architecture.md#how-a-request-moves-through-it) {fa-solid fa-microphone-lines}
  From Home Assistant Assist through Marshal and back.
- [Confirm-tier action](../concepts/risk-and-confirmation.md#frozen-pending-actions) {fa-solid fa-hand}
  Frozen with a nonce and an expiry, confirmed only by a user action.
- [Task lifecycle](../concepts/tasks-and-scheduling.md#the-lifecycle-of-a-run) {fa-solid fa-list-check}
  From trigger through worker, approval and delivery.
- [Data model](erd.md)
:::
