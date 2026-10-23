---
icon: fa-solid fa-diagram-next
description: "Marshals Regeln für OpenSpec-Änderungen: was jedes Artefakt enthalten muss und was eine Änderung außer Code aktualisiert."
---

# OpenSpec-Ablauf

Marshals Regeln für OpenSpec-Änderungen. Nutz die Skills `opsx:*` (`propose`, `explore`, `apply`, `sync`, `archive`) wie gewohnt.

## Regeln

- `openspec/specs/` ist maßgeblich. Widersprechen sich Code und Spec, korrigier den Code oder schlag eine Änderung der Spec vor; weich nie stillschweigend ab.
- Keine Implementierung ohne freigegebene Änderung in `openspec/changes/`, die sie abdeckt.

## Regeln für Artefakte

`openspec/config.yaml` ergänzt jede Änderung um diese Regeln:

| Artefakt | Muss enthalten |
|---|---|
| Proposal | Einen Abschnitt „Non-goals“. |
| Design | Jede neue Schnittstelle und ihre v1-Implementierungen. Für jedes neue Werkzeug des Modells: seine Risikostufe (`safe`, `confirm`, `forbidden`, auch argumentabhängige Regeln) und wann es vor dem Modell verborgen ist. |
| Tasks | Test zuerst: Jeder Implementierungs-Task folgt dem Task, der seinen fehlschlagenden Test schreibt, oder ist mit ihm gepaart. |

## Fertig heißt

- Jede Anforderung in den Delta-Specs hat ein Szenario und einen Test.
- Dieselbe Änderung aktualisiert `CLAUDE.md` (Karte des Repositorys, Paketindex), die [Diagramme](../diagrams/README.md), die Doku-Seiten zum geänderten Verhalten ([Stil der Dokumentation](docs-style.md)) und `Marshal.Demo`, wenn es eine geänderte Schnittstelle implementiert.
- Die Änderung ist synchronisiert und archiviert, damit `openspec/specs/` zeigt, was ausgeliefert wurde.

## Verwandte Seiten

::: cards
- [Eine Integration hinzufügen](adding-an-integration.md)
- [Ein Werkzeug hinzufügen](adding-a-tool.md)
:::
