---
title: Diagrams
icon: fa-solid fa-diagram-project
order: 95
description: PlantUML diagrams of the packages, the data model and the main flows, rendered from the sources in this folder.
---

# Diagrams

PlantUML diagrams of the packages, the data model and the main flows, rendered from the sources in this folder. How to keep them current and render them is in [maintaining the diagrams](README.md).

## Packages

![Marshal package diagram](packages.svg)

## Data model

![Entity relationship diagram](erd.svg)

## Voice turn

![Voice turn from Home Assistant Assist through Marshal and back](request-flow.svg)

## Confirm-tier action

![Confirm-tier action frozen with nonce and TTL, confirmed by a user turn, the web UI or a push action](confirm-flow.svg)

## Task lifecycle

![Scheduled or triggered task through worker, approval and delivery](task-lifecycle.svg)
