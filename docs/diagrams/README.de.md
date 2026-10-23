---
title: Die Diagramme pflegen
icon: fa-solid fa-pen-ruler
description: "Wo die Mermaid-Diagramme liegen, wie klein sie bleiben, wann du sie aktualisierst und wie du sie in der Vorschau ansiehst."
---

# Die Diagramme pflegen

Die Diagramme sind [Mermaid](https://mermaid.js.org)-Snippets in `docs/_diagrams/`, eingebunden mit `<!--@include: ../_diagrams/<name>.md-->`; jedes Diagramm hat also eine Datei zum Bearbeiten. Auf GitHub zeigt die einbindende Seite nichts, weil das Include ein HTML-Kommentar ist.

| Diagramm | Snippets | Sprache | Seite |
|---|---|---|---|
| Projektverweise | `packages.md` | Eine Datei | [Architektur](../concepts/architecture.md#die-teile) |
| Verbindungen zur Laufzeit | `packages-runtime.md` | Eine Datei | [Architektur](../concepts/architecture.md#die-teile) |
| Sprach-Turn | `voice-turn-request`, `voice-turn-agent-loop` | Eine Datei pro Sprache | [Architektur](../concepts/architecture.md#wie-eine-anfrage-hindurchläuft) |
| Aktion der Bestätigungsstufe | `confirm-tier-freeze`, `confirm-tier-confirm` | Eine Datei pro Sprache | [Risiko und Bestätigung](../concepts/risk-and-confirmation.md#eingefrorene-ausstehende-aktionen) |
| Lebenszyklus einer Aufgabe | `task-lifecycle`, `task-run` | Eine Datei pro Sprache | [Aufgaben und Zeitplanung](../concepts/tasks-and-scheduling.md#der-lebenszyklus-eines-durchlaufs) |
| Datenmodell | `erd-<area>-<part>.md` | Eine Datei | [Datenmodell](erd.md) |

Diagramme des Codes (Namen von Projekten, Entitäten und Spalten) werden nie übersetzt. Ablaufdiagramme haben eine deutsche Kopie (`<name>.de.md`) mit denselben Teilnehmern, Zuständen und Pfeilen in derselben Reihenfolge; ändere beide im selben Commit.

## Größe

Die Inhaltsspalte der Website ist etwa 640 px breit. Clay zeichnet die meisten Diagramme beim Build (`beautiful-mermaid`) und verkleinert ein breiteres, bis seine Schrift schwer lesbar ist. Halt jedes Diagramm bei höchstens etwa 800 px Breite:

- Beginn den Block mit dem Diagrammtyp (`sequenceDiagram`, `erDiagram`, ...), danach die `%%`-Kommentare. Ein Block, der nicht mit seinem Typ beginnt, wird stattdessen im Browser gezeichnet, ohne das Theme der Website.
- Teil einen Ablauf in Teile mit höchstens fünf Teilnehmern oder etwa acht Zuständen, jeden mit eigenem Snippet.
- Sequenzdiagramme: eine kurze Zeile pro Nachricht (etwa 30 Zeichen), kein `<br/>` in Nachrichten und keine Notizen zwischen Nachrichten; der Renderer beim Build reserviert dafür keinen Platz, sie überlappen also. Details gehören in den Text der Seite.
- ER-Diagramme werden immer von links nach rechts gezeichnet, ihre Breite ist also die Zahl der Spalten. Halt sie bei zwei oder drei: Kanten zum Eigentümer (`owner_id` zu `User`) sind nicht gezeichnet, und eine Beziehung steht nur im Diagramm der Entität, die den Schlüssel hält. Entitäten aus anderen Diagrammen erscheinen als einfache Kästen.
- Externe Pakete sind nicht gezeichnet. Der Paketindex in `CLAUDE.md` listet sie.

## Aktuell halten

Die Diagramme müssen immer zum Code passen. Jedes Snippet hat einen Kommentar `%% KEEP UP TO DATE`, der sagt, was es beschreibt.

- **Datenmodell:** Aktualisiere die Snippets `erd-<area>-<part>.md` des [Datenmodells](erd.md) in derselben Änderung, die eine EF-Core-Entität, -Spalte oder -Beziehung hinzufügt, entfernt oder umbenennt. Setz die Entität in das Diagramm ihres Bereichs und ergänze Tabellennamen, Indizes und Einschränkungen in der Tabelle am Ende von `erd.md` und ihrer Übersetzung.
- **Pakete:** Aktualisiere `packages.md` in derselben Änderung, die ein Projekt in `src/` hinzufügt, entfernt oder umbenennt oder einen Projektverweis ändert, und `packages-runtime.md`, wenn ein Client oder getrennter Dienst sich anders verbindet.
- **Abläufe:** Aktualisiere die Snippets für Sprach-Turn, Bestätigungsstufe und Aufgaben, englisch und deutsch, in derselben Änderung, die das gezeigte Verhalten ändert.

## Vorschau

- `npx clay-doc@5 dev` zeichnet die Diagramme mit Live-Reload (Node.js 22 oder neuer).
- `npx clay-doc@5 check` zeichnet keine Diagramme und findet daher keine Syntaxfehler in Mermaid. Sieh dir die Seite im Entwicklungsserver an oder füg den Block in den [Mermaid Live Editor](https://mermaid.live) ein.
- Editoren: Die Markdown-Vorschau von VS Code (mit einer Mermaid-Erweiterung) und die JetBrains-IDEs zeichnen `mermaid`-Blöcke.
