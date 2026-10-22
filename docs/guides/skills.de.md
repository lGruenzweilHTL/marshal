---
icon: fa-solid fa-wand-magic-sparkles
description: "Schreib einen Skill, um Marshal einen Ablauf beizubringen, und mach ihn den richtigen Agent-Templates verfügbar."
---

# Skills

Schreib einen Skill, um Marshal einen Ablauf beizubringen, und mach ihn den richtigen Agent-Templates verfügbar.

## Bevor du anfängst

- Zugriff auf das Volume `/config`. Skills sind vertrauenswürdige Konfiguration, die du schreibst; das Laden eines Skills belastet nie einen Durchlauf.
- Vollständige Referenz des Formats: [Skill-Format](../configuration/skills-format.md).

## Schritte

::: steps
1. **Leg das Verzeichnis und `SKILL.md` an**

   Ein Skill ist ein Verzeichnis `/config/skills/<name>/` mit einer `SKILL.md`.

   ```markdown
   ---
   name: house-status
   description: Reports what in the house needs attention. Use when asked how the house is doing.
   ---

   # House status

   1. Call `ha_homeassistant__GetLiveContext`.
   2. List open windows, low batteries and finished appliances in at most five short lines.
   ```

   Regeln, die beim Laden geprüft werden:

   | Regel | Fehlertext (mit dem Präfix `<name>/SKILL.md:`) |
   |---|---|
   | Frontmatter vorhanden | `has no YAML frontmatter (--- name / description ---).` |
   | `name` gesetzt, 1 bis 64 Kleinbuchstaben, Ziffern und einzelne Bindestriche | `name is required.` / `name '<x>' must be 1-64 lowercase letters, digits and single hyphens, not starting or ending with a hyphen.` |
   | `name` gleich dem Verzeichnisnamen | `name '<x>' must match its directory '<dir>'.` |
   | `description` gesetzt, höchstens 1024 Zeichen | `description is required.` |
   | Keine unbekannten Frontmatter-Schlüssel | `invalid frontmatter at line <n>: ...` |
   | Keine Secrets | Der Secret-Scanner weist Schlüssel und Werte ab, die wie Zugangsdaten aussehen |

   Optionale Frontmatter-Schlüssel: `license`, `compatibility` (höchstens 500 Zeichen), `metadata` und `allowed-tools`. Schreib den Auslöser in die `description`: Das Modell sieht nur Name und Beschreibung, bis es den Skill lädt.

2. **Füge weitere Dateien hinzu (optional)**

   Andere Textdateien im Skill-Verzeichnis (bis 256 KB je Datei) lädt das Modell bei Bedarf mit `agent_load_skill` und einem Argument `file`, zum Beispiel `references/format.md`. Das Ergebnis des Werkzeugs listet sie.

3. **Mach ihn verfügbar**

   Ein Template listet die Skills, die es nutzen darf, in seinem Schlüssel `skills`; ohne `skills` sieht ein Durchlauf alle Skills. Das Template `default` (Turns von Unterhaltungen) sieht alle. Für ein Template mit Allowlist fügst du den Skill-Namen und das Werkzeug `agent.load_skill` zu seinen `tools` hinzu, siehe [Agent-Templates](agent-templates.md).

   Marshal stellt nur den Index (Name und Beschreibung) in den Prompt, und das Modell lädt den Inhalt, wenn es ihn braucht. `agent.load_skill` ist verborgen, wenn dem aktuellen Template kein Skill zur Verfügung steht.

4. **Neu laden**

   Skills laden im laufenden Betrieb neu. Ein ungültiger Skill wird mit einem Fehler im Log und in der Konfigurationsansicht übersprungen; andere Skills laden weiter.
:::

## Prüfen, ob es funktioniert

Öffne die Konfigurationsansicht in der Weboberfläche (**System**, dann Konfiguration), um abgewiesene Skills zu sehen, frag Marshal dann etwas, das der Skill abdeckt, und öffne den Durchlauf: Du siehst einen Werkzeugschritt `agent_load_skill`.

## Fehlerbehebung

- Skill wird nicht angeboten: Die Liste `skills` des Templates lässt ihn aus, oder er wurde abgewiesen. Siehe [Fehlerbehebung](troubleshooting.md).
- `There is no skill '<name>'. Available: ...`: Der Name ist falsch, oder das Template erlaubt den Skill nicht.

## Verwandte Seiten

::: cards
- [Agent-Templates](agent-templates.md)
- [Tägliches Briefing](tasks/daily-briefing.md)
- [Skill-Format](../configuration/skills-format.md)
:::
