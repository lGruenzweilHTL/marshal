---
icon: fa-solid fa-wand-magic-sparkles
description: "Ein Skill ist ein Verzeichnis /config/skills/<name>/ mit einer Datei SKILL.md im Agent-Skills-Format, plus optionalen zusätzlichen Dateien."
---

# Skills (`skills/*/SKILL.md`)

Ein Skill ist ein Verzeichnis `/config/skills/<name>/` mit einer Datei `SKILL.md` im Agent-Skills-Format, plus optionalen zusätzlichen Dateien. Marshal lädt Skills ohne Neustart neu.

Skills sind vom Betreiber geschriebene Konfiguration, keine nicht vertrauenswürdigen Inhalte. Einen zu laden belastet den Durchlauf nicht. Skills dürfen keine Secrets enthalten.

## Beispiel

```markdown
---
name: house-status
description: "Gives a short status report of the house. Use when the user asks how the house is."
---

# House status

1. Call `ha_homeassistant__GetLiveContext` once.
2. Report only what needs attention, most important first.
```

## Frontmatter

Die Datei muss mit einer Zeile `---`, YAML und einer schließenden Zeile `---` beginnen. Die Schlüssel stehen genau wie in der Tabelle (Kleinbuchstaben, `allowed-tools` mit Bindestrich). Unbekannte Schlüssel sind Fehler.

| Schlüssel | Typ | Pflicht | Beschreibung |
|---|---|---|---|
| `name` | Zeichenkette | ja | 1 bis 64 Kleinbuchstaben, Ziffern und einzelne Bindestriche; kein Bindestrich am Anfang oder Ende. Muss dem Verzeichnisnamen gleichen. |
| `description` | Zeichenkette | ja | Was der Skill tut und wann er zu nutzen ist, höchstens 1024 Zeichen. Erscheint im Skill-Index. |
| `license` | Zeichenkette | nein | Wird akzeptiert und ignoriert. |
| `compatibility` | Zeichenkette | nein | Höchstens 500 Zeichen. Wird akzeptiert und ignoriert. |
| `metadata` | Abbildung von Zeichenkette auf Zeichenkette | nein | Wird akzeptiert und ignoriert. |
| `allowed-tools` | Zeichenkette | nein | Wird akzeptiert und ignoriert. Den Zugriff auf Werkzeuge legen [Templates](templates-format.md) fest. |

Der Body (alles nach dem Frontmatter) ist der Anweisungstext.

## Zusätzliche Dateien

Jede andere Datei im Skill-Verzeichnis, auch in Unterordnern, ist über `agent_load_skill` lesbar. Dateien über 256 KiB und Binärdateien (mit einem Nullzeichen) werden übersprungen. Alle Dateien werden nach wörtlichen Secrets durchsucht.

## Wie das Modell Skills nutzt

Der Prompt enthält nur den Index (Name und Beschreibung) der Skills, die dem Template zur Verfügung stehen. Das Modell ruft `agent_load_skill(name)` für den Body auf und `agent_load_skill(name, file)` für eine andere Datei. Das Werkzeug ist verborgen, wenn kein Skill verfügbar ist. Ein Template begrenzt Skills mit seiner Liste `skills` ([Templates](templates-format.md)).

## Validierung

Ein abgelehnter Skill wird mit einem Fehler übersprungen; andere Skills laden. Ein Verzeichnis ohne `SKILL.md` wird ignoriert.

- `<name>/SKILL.md: has no YAML frontmatter (--- name / description ---).`
- `<name>/SKILL.md: name is required.`
- `<name>/SKILL.md: name '<x>' must be 1-64 lowercase letters, digits and single hyphens, not starting or ending with a hyphen.`
- `<name>/SKILL.md: name '<x>' must match its directory '<dir>'.`
- `<name>/SKILL.md: description is required.`
- `<name>/SKILL.md: description must be at most 1024 characters.`
- `<name>/SKILL.md: compatibility must be at most 500 characters.`
- `<name>/SKILL.md: invalid frontmatter at line <n>: ...`
- Ein wörtliches Secret im Frontmatter, im Body oder in einer Datei.

## Verwandte Seiten

::: cards
- [Anleitung zu Skills](../guides/skills.md)
- [Agent-Templates](templates-format.md)
:::
