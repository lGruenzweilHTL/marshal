---
icon: fa-solid fa-diagram-next
description: "Eine Änderung vorschlagen, umsetzen, abgleichen und archivieren."
---

# OpenSpec-Ablauf

Eine Änderung vorschlagen, umsetzen, abgleichen und archivieren. Marshal ist spezifikationsgetrieben: Verhalten wird in `openspec/specs/` festgelegt, bevor es gebaut wird.

## Regeln

- `openspec/specs/` ist maßgeblich. Widersprechen sich Code und Spezifikation, gewinnt die Spezifikation. Korrigiere den Code oder schlag eine Änderung der Spezifikation vor; weiche nie stillschweigend ab.
- Fang keine Implementierung ohne eine freigegebene Änderung in `openspec/changes/` an, die sie abdeckt.

## Aufbau

| Pfad | Enthält |
|---|---|
| `openspec/specs/<capability>/spec.md` | Die aktuellen Anforderungen und Szenarien. |
| `openspec/changes/<change>/` | Eine vorgeschlagene oder laufende Änderung: Proposal, Design, Delta-Spezifikationen, Tasks. |
| `openspec/changes/archive/` | Abgeschlossene Änderungen. |
| `openspec/config.yaml` | Projektkontext und die Regeln, die jedes Artefakt einhalten muss. |

## Schritte

::: steps
1. **Vorschlagen**

   Nutze den Skill `opsx:propose` (oder den Skill `openspec-propose`), um die Änderung anzulegen und ihre Artefakte in einem Schritt zu erzeugen. `openspec/config.yaml` ergänzt diese Regeln:

   - Das Proposal hat einen Abschnitt „Non-goals“.
   - Das Design nennt jede neue Schnittstelle und ihre v1-Implementierungen.
   - Für jedes neue Werkzeug für das Modell nennt das Design seine Risikostufe (safe, confirm oder forbidden, einschließlich Regeln, die die Argumente berücksichtigen) und wann das Werkzeug vor dem Modell verborgen ist.
   - Tasks sind Test-first: Vor jeder Implementierungs-Task steht die Task, die ihren fehlschlagenden Test schreibt, oder beide gehören zusammen.

   Nutze `opsx:explore`, um ein Problem zu durchdenken, bevor du etwas vorschlägst.

2. **Umsetzen**

   Arbeite die Tasks mit `opsx:apply` ab. Schreib den fehlschlagenden Test, bring ihn zum Bestehen und hake dann die Task ab. Aktualisiere `CLAUDE.md` (Karte des Repositorys, Paketindex), die Diagramme und die Dokumentation in derselben Änderung, wenn die Änderung es verlangt.

3. **Abgleichen**

   Nutze `opsx:sync`, um die Delta-Spezifikationen der Änderung in `openspec/specs/` zu übernehmen, ohne die Änderung zu archivieren.

4. **Archivieren**

   Nutze `opsx:archive`, wenn die Implementierung fertig ist. Der Skill verschiebt die Änderung nach `openspec/changes/archive/` und gleicht die Spezifikationen ab, falls du das noch nicht getan hast.
:::

## Prüfen, ob es funktioniert

- Jede Anforderung in den Delta-Spezifikationen hat ein Szenario und einen Test.
- `openspec/specs/` spiegelt das Verhalten, das du ausgeliefert hast.
- Die Dokumentationsseiten für das geänderte Verhalten passen zur Spezifikation ([Stil der Dokumentation](docs-style.md)).

## Verwandte Seiten

::: cards
- [Überblick zur Entwicklung](index.md)
- [Eine Integration hinzufügen](adding-an-integration.md)
:::
