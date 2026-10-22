---
icon: fa-solid fa-shield-halved
description: "Lege fest, welche Aktionen deine Bestätigung brauchen, indem du /config/risk.yaml bearbeitest, ausgehend von der Beispieldatei."
---

# Risikoregeln

Lege fest, welche Aktionen deine Bestätigung brauchen, indem du `/config/risk.yaml` bearbeitest, ausgehend von der Beispieldatei.

## Bevor du anfängst

- Zugriff auf das Volume `/config`. `risk.yaml` lädt im laufenden Betrieb neu: Der nächste Werkzeugaufruf nutzt die neuen Regeln. Eine Regeldatei, die die Validierung nicht besteht, wird ignoriert, die vorherigen Regeln bleiben aktiv, und der Fehler wird protokolliert.
- Referenz der Schlüssel: [`risk.yaml`](../configuration/risk-yaml.md). Definition der Stufen: [Risikostufen](../reference/risk-tiers.md).

## Wie der Guard entscheidet

Jeder Werkzeugaufruf hat eine Stufe: `safe` (läuft), `confirm` (läuft erst, nachdem eine Benutzeraktion ihn bestätigt) oder `forbidden` (läuft nie). Regeln werden der Reihe nach ausgewertet, der erste Treffer gewinnt, und ein Werkzeug ohne passende Regel nutzt seine Standardstufe. Externe Werkzeuge (MCP-Server) haben standardmäßig `confirm`. Regeln gelten für eingebaute und externe Werkzeuge gleich.

Werkzeuge, die das Modell nicht nutzen darf, bekommt es nicht zu sehen: `forbidden`-Werkzeuge werden aus der Werkzeugliste gefiltert, und der Guard setzt dieselben Regeln als zweite Linie auf dem Server durch. Fähigkeiten, die nie existieren dürfen (Mail senden, rohes G-Code), sind gar keine Werkzeuge, also kann keine Regel sie freischalten.

Der Guard kann eine Stufe nur anheben, nie senken. Belastete Durchläufe (die Mail, RSS, Notizen, Webseiten oder Ereignisdaten gelesen haben) heben `persists`-Werkzeuge wie `memory.remember` und `tasks.create` von `safe` auf `confirm`, egal, was deine Regeln sagen.

## Die Beispieldatei

```yaml
risk:
  - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
    tier: confirm
  - match: { tool: "ha.*", domain: cover, deviceClass: [garage, door, gate] }
    tier: confirm
  - match: { tool: "ha.*" }
    tier: safe
```

Von oben gelesen: Jeder Home-Assistant-Aufruf, der ein Schloss oder eine Alarmanlage betrifft, braucht eine Bestätigung; Abdeckungen mit der Geräteklasse garage, door oder gate brauchen eine Bestätigung; jeder andere Home-Assistant-Aufruf ist sicher. Ohne die letzte Regel hätten die MCP-Werkzeuge von Home Assistant standardmäßig `confirm`.

Zielbedingungen (`domain`, `deviceClass`, `area`) werden gegen jede konkrete Entität geprüft, auf die ein Aufruf auflöst, und die höchste Stufe gewinnt. Ein Aufruf für einen Bereich mit zwei Lichtern und einem Schloss ist als Ganzes `confirm`. Ein Aufruf mit unbekanntem Gerätenamen, ohne aufgelöste Ziele oder mit einem Namensraum ohne Resolver, während eine Zielregel passt, ist `confirm` mit dem Grund `I couldn't tell exactly which device you mean.`

## Schritte

::: steps
1. **Füg eine Regel über der Auffangregel hinzu**

   Regeln passen auf ein Glob-Muster der Werkzeug-ID, optional auf Argumentwerte und Zielattribute. Sie können nicht auf die Tageszeit passen. Beispiele:

   ```yaml
   risk:
     # das Modell darf das Garagentor nie öffnen, auch nicht nach einer Bestätigung
     - match: { tool: "ha.*", domain: cover, deviceClass: garage }
       tier: forbidden
     # alles in der Werkstatt bestätigen
     - match: { tool: "ha.*", area: workshop }
       tier: confirm
     - match: { tool: "ha.*", domain: [lock, alarm_control_panel] }
       tier: confirm
     - match: { tool: "ha.*" }
       tier: safe
   ```

   Stell spezielle Regeln über allgemeine. Prüfe die Werkzeug-IDs in der [Werkzeug-Referenz](../reference/tools.md); `tool` ist ein Glob-Muster wie `ha.*` oder `mail.*`.

2. **Speichere und prüfe das Log**

   Ist die Datei ungültig, zeigt das Log zum Beispiel `risk.yaml: risk[2].tier must be safe, confirm or forbidden.` Siehe die [Validierungsliste von risk.yaml](../configuration/risk-yaml.md#validierung).

3. **Stimme das Bestätigungsverhalten ab**

   Die Bestätigung selbst stellst du in [`confirmation`](../configuration/marshal-yaml/confirmation.md) ein:

   - Eine ausstehende Aktion in einer Unterhaltung gilt `confirmation.ttl` lang (Standard 60 s); es gibt höchstens eine pro Sitzung, und eine neue ersetzt die alte.
   - Deine nächste Äußerung wird normalisiert (getrimmt, kleingeschrieben, Satzzeichen entfernt) und mit den Schlüsselwörtern deiner Sprache verglichen (englische Standards: confirm, confirmed, approved, yes do it). Nur ein exakter Treffer der ganzen Äußerung führt ohne Modellaufruf aus. „Bestätigt, aber nicht heute“ ist kein Treffer.
   - Sonst liest ein Prüfer der Stufe fast nur die Zusammenfassung der Aktion und deine Antwort und antwortet `positive` (ausführen), `neutral` (verwerfen und deine Nachricht normal behandeln) oder `negative` (verwerfen und sagen, dass abgebrochen wurde). Eine Zeitüberschreitung des Prüfers (`confirmation.judgeTimeout`) oder eine unlesbare Antwort verwirft die Aktion.
   - Hintergrundaufgaben nutzen stattdessen die Freigabe-Warteschlange, siehe [Freigaben](tasks/approvals.md).

   Satelliten können außerdem Aktionen der Bestätigungsstufe von einem Gerät verweigern (`denyConfirm` in [Satelliten](../configuration/satellites-yaml.md)): `This action is not allowed from this device.`
:::

## Prüfen, ob es funktioniert

Bitte Marshal um etwas, das deine Regel abdeckt. Öffne unter **Läufe** den Werkzeugschritt: Das Detail zeigt die Risikostufe und die Entscheidung des Guards (`safe`, `pending`, `confirmed_by_keyword`, `judge_positive`, `judge_neutral`, `judge_negative`, `escalated_by_taint`, `denied`, `expired`).

## Verwandte Seiten

::: cards
- [Risiko und Bestätigung](../concepts/risk-and-confirmation.md)
- [Sichtbarkeit von Werkzeugen](../concepts/tool-visibility.md)
- [Sicherheitsmodell](../concepts/security-model.md)
- [`risk.yaml`](../configuration/risk-yaml.md)
- [Risikostufen](../reference/risk-tiers.md)
:::
