---
title: Die Diagramme pflegen
icon: fa-solid fa-pen-ruler
description: "Wo die Mermaid-Diagramme liegen, wann du sie aktualisierst und wie du sie in der Vorschau ansiehst."
---

# Die Diagramme pflegen

Die Diagramme sind [Mermaid](https://mermaid.js.org)-Codeblöcke in Snippet-Dateien in `docs/_diagrams/`. Die Seiten binden sie mit `<!--@include: ../_diagrams/<name>.md-->` ein, jedes Diagramm hat also eine Datei zum Bearbeiten, egal welche Seiten und Sprachen es zeigen. Clay zeichnet sie auf der Website. GitHub zeichnet sie, wenn es die Snippet-Datei anzeigt; die Seite selbst zeigt dort nichts, weil das Include ein HTML-Kommentar ist.

| Diagramm | Snippet | Sprache |
|---|---|---|
| Pakete | `packages.md` | Eine Datei für alle Sprachen: Sie enthält Namen von Projekten und Paketen |
| Datenmodell | `erd-<area>.md` | Eine Datei für alle Sprachen: Sie enthält Namen von Entitäten und Spalten |
| Sprach-Turn | `voice-turn.md`, `voice-turn.de.md` | Eine Datei pro Sprache |
| Aktion der Bestätigungsstufe | `confirm-tier-action.md`, `confirm-tier-action.de.md` | Eine Datei pro Sprache |
| Lebenszyklus einer Aufgabe | `task-lifecycle.md`, `task-lifecycle.de.md` | Eine Datei pro Sprache |

Diagramme des Codes (Namen von Projekten, Paketen, Entitäten, Spalten) werden nie übersetzt: Die Namen sind der Code. Ablaufdiagramme haben Beschriftungen in Prosa, die deutschen Seiten binden also eine deutsche Kopie ein. Ändere beide Dateien eines Ablaufs im selben Commit; sie behalten dieselben Teilnehmer, Zustände und Pfeile in derselben Reihenfolge, ein Diff nebeneinander zeigt also, was sich unterscheidet.

| Diagramm | Seite |
|---|---|
| Pakete | [Architektur](../concepts/architecture.md#die-teile) |
| Sprach-Turn | [Architektur](../concepts/architecture.md#wie-eine-anfrage-hindurchläuft) |
| Aktion der Bestätigungsstufe | [Risiko und Bestätigung](../concepts/risk-and-confirmation.md#eingefrorene-ausstehende-aktionen) |
| Lebenszyklus einer Aufgabe | [Aufgaben und Zeitplanung](../concepts/tasks-and-scheduling.md#der-lebenszyklus-eines-durchlaufs) |
| Datenmodell, ein Diagramm pro Bereich | [Datenmodell](erd.md) |

## Aktuell halten

Die Diagramme müssen immer zum Code passen. Jedes Snippet beginnt mit einem Kommentar `%% KEEP UP TO DATE`, der sagt, was es beschreibt.

- **Datenmodell:** Aktualisiere die Snippets `erd-<area>.md` des [Datenmodells](erd.md) in derselben Änderung, die eine EF-Core-Entität, -Spalte oder -Beziehung hinzufügt, entfernt oder umbenennt. Setz die Entität in das Diagramm ihres Bereichs und ergänze Tabellennamen, Indizes und Einschränkungen in der Tabelle am Ende von `erd.md` und ihrer Übersetzung.
- **Pakete:** Aktualisiere `packages.md` (gezeigt unter [Architektur](../concepts/architecture.md)) in derselben Änderung, die ein Projekt in `src/`, `web/` oder `ha/` hinzufügt, entfernt oder umbenennt oder einen Projektverweis oder eine wichtige externe Abhängigkeit ändert.
- **Abläufe:** Aktualisiere die Snippets für Sprach-Turn, Bestätigungsstufe und Lebenszyklus einer Aufgabe, englisch und deutsch, in derselben Änderung, die das gezeigte Verhalten ändert.

## Vorschau

- `npx clay-doc@5 dev` zeichnet die Diagramme mit Live-Reload (Node.js 22 oder neuer).
- `npx clay-doc@5 check` zeichnet keine Diagramme und findet daher keine Syntaxfehler in Mermaid. Sieh dir die Seite im Entwicklungsserver an oder füg den Block in den [Mermaid Live Editor](https://mermaid.live) ein.
- Editoren: Die Markdown-Vorschau von VS Code (mit einer Mermaid-Erweiterung) und die JetBrains-IDEs zeichnen `mermaid`-Blöcke.
