---
icon: fa-solid fa-seedling
description: "Lass Marshal Anfragen bemerken, die du wiederholst, einen Skill dafür entwerfen, und füg die Skills, die du willst, zu /config/skills hinzu."
---

# Wiederkehrende Abläufe zu Skills machen

Lass Marshal Anfragen bemerken, die du wiederholst, einen Skill dafür entwerfen, und füg die Skills, die du willst, zu `/config/skills` hinzu.

## Bevor du anfängst

- Melde dich in der Weboberfläche an.
- Du brauchst Schreibzugriff auf `/config/skills` auf dem Host. Marshal schreibt nie selbst nach `/config`.
- Lies [Skills](skills.md), um zu wissen, was ein Skill ist und wie Marshal ihn lädt.

## Schritte

::: steps
1. **Schalte „Marshal beim Lernen helfen“ ein**

   Öffne **Präferenzen** und schalte **Marshal beim Lernen helfen** ein. Der nächtliche Job läuft nur für Eigentümer mit eingeschaltetem Schalter.

2. **Lass den nächtlichen Job Abläufe finden**

   Jede Nacht um 04:00 (`maintenance.skillProposalSchedule`) sieht sich Marshal deine Durchläufe der letzten 30 Tage an. Ein Ablauf ist eine Gruppe von mindestens drei Durchläufen, die:

   - mit `ok` geendet und mindestens drei Werkzeuge aufgerufen haben,
   - nicht negativ bewertet wurden,
   - dieselben Werkzeuge in derselben Reihenfolge aufgerufen haben,
   - mit ähnlichen Anfragen begonnen haben.

   Ein Modell der Stufe `standard` entwirft für jeden Ablauf eine `SKILL.md`. Es sieht deine Anfragen und die Namen der Werkzeuge und Argumente, nie Werkzeugergebnisse oder Argumentwerte. Deckt ein vorhandener Skill den Ablauf schon ab, wird nichts entworfen. Ein Ablauf, den du verworfen hast, wird nie wieder vorgeschlagen.

   Gibt es neue Entwürfe, fasst eine `low`-Benachrichtigung sie zusammen.

3. **Prüfe einen Vorschlag**

   Öffne den **Eingang**. Skill-Vorschläge stehen unter **Skill-Vorschläge**. Öffne einen, um zu sehen:

   - die entworfene `SKILL.md`,
   - den Diff zu einem vorhandenen Skill mit demselben Namen, falls es einen gibt,
   - Links zu den Durchläufen, aus denen der Ablauf stammt.

4. **Füg den Skill hinzu oder verwirf ihn**

   - Klicke auf **SKILL.md herunterladen**. Speichere die Datei als `/config/skills/<name>/SKILL.md`, mit dem Namen aus dem Vorschlag. Bearbeite sie vorher, wenn du willst. Marshal lädt sie, wenn sich der Ordner ändert.
   - Klicke auf **Verwerfen**, um den Vorschlag fallen zu lassen. Derselbe Ablauf wird nicht noch einmal vorgeschlagen.
:::

## Prüfen, ob es funktioniert

Öffne nach dem Speichern **System**, dann die Konfigurationsansicht, und prüfe, dass der Skill nicht als abgewiesen gelistet ist. Frag erneut nach dem Ablauf; das Detail des Durchlaufs zeigt einen Werkzeugschritt `agent_load_skill`.

## Verwandte Seiten

::: cards
- [Skills](skills.md)
- [Skill-Format](../configuration/skills-format.md)
- [Konfiguration von `maintenance`](../configuration/marshal-yaml/maintenance.md)
- [Vorschläge bekommen](suggestions.md)
:::
