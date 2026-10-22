---
icon: fa-solid fa-lightbulb
description: "Lass Marshal Dinge zu Hause bemerken und vorschlagen, was zu tun ist, etwa ein Fenster vor Regen zu schließen, und bring ihm bei, welche Vorschläge du willst."
---

# Vorschläge bekommen

Lass Marshal Dinge zu Hause bemerken und vorschlagen, was zu tun ist, etwa ein Fenster vor Regen zu schließen, und bring ihm bei, welche Vorschläge du willst.

## Bevor du anfängst

- Melde dich in der Weboberfläche an.
- Verbinde [Home Assistant](integrations/home-assistant.md). Die Druckervorschläge brauchen außerdem [Moonraker-Drucker](integrations/printers-moonraker.md).
- Setz deine Home-Assistant-Entität `person` unter **Präferenzen** in **Benachrichtigungen**, damit Marshal weiß, wann du weg bist.
- Für Vorschläge per Push setzt du in derselben Karte ein Push-Ziel. Siehe [Benachrichtigungen](notifications.md).

## Schritte

::: steps
1. **Schalte „Marshal beim Lernen helfen“ ein**

   Öffne **Präferenzen** und schalte **Marshal beim Lernen helfen** ein. Standardmäßig ist es aus. Ohne diese Option läuft für dich kein Detektor, und kein Vorschlag wird gesendet. Ausschalten stoppt neue Vorschläge sofort; offene bleiben, bis sie ablaufen.

2. **Lerne die Detektoren kennen**

   | Detektor | Schlägt vor | Vorgeschlagene Aktion |
   |---|---|---|
   | `window_rain` | Ein offenes Fenster oder eine offene Tür, während innerhalb der Stunde wahrscheinlich Regen kommt. | Keine. |
   | `energy_anomaly` | Ein Gerät im Energie-Dashboard, das gestern mindestens das 1,5-Fache seiner üblichen Tagesenergie verbraucht hat. | Keine. |
   | `print_done_away` | Ein Druck ist fertig geworden, während du weg bist. | Den Drucker ausschalten, wenn `printers[].powerEntity` gesetzt ist. |
   | `maintenance_counter` | Ein Zähler ohne [Wartungsobjekt](maintenance.md), zum Beispiel die Druckstunden eines Druckers. | Keine. Der Vorschlag zeigt ein YAML-Snippet zum Kopieren. |

   Detektoren brauchen die Integrationen, die sie lesen. Ein Detektor ohne seine Daten bleibt still. Um Schwellen zu ändern oder einen Detektor für alle auszuschalten, siehe [`suggestions`](../configuration/marshal-yaml/suggestions.md).

3. **Beantworte Vorschläge**

   Vorschläge erscheinen im **Eingang** unter **Vorschläge**, nach Freigaben und Änderungsvorschlägen und vor Benachrichtigungen. Jeder zeigt seinen Text und, falls vorhanden, die Aktion, die **Annehmen** ausführt.

   - **Annehmen** führt die vorgeschlagene Aktion einmal aus, über den Guard. Ist die Aktion in der Bestätigungsstufe, sagt die Karte, warum, und Annehmen zählt als deine Bestätigung.
   - **Verwerfen** führt nichts aus.

   Ein Vorschlag per Push hat dieselben Schaltflächen **Annehmen** und **Verwerfen** in der Benachrichtigung. Vorschläge nutzen nie Sprache und beachten deine Ruhezeiten. Ein unbeantworteter Vorschlag läuft nach 6 Stunden ab, oder früher, wenn seine Bedingung nicht mehr gilt, zum Beispiel wenn du das Fenster selbst geschlossen hast.

4. **Lass Marshal lernen, was du willst**

   Marshal zählt deine Antworten pro Detektor und schätzt, wie wahrscheinlich du den nächsten Vorschlag annimmst: (angenommen + 1) / (angenommen + verworfen + 2).

   - Die ersten 3 Vorschläge eines Detektors gehen unabhängig von der Schätzung in den Eingang.
   - Danach wird ein Vorschlag mit einer Schätzung unter 0.3 verworfen.
   - Ab 0.3 geht er mit Priorität `low` in den Eingang.
   - Ab 0.6 wird er mit Priorität `normal` per Push gesendet.

   Abgelaufene Vorschläge ändern die Zähler nicht.

5. **Schalte einen Detektor stumm oder setz ihn zurück**

   Öffne **Präferenzen** und such die Karte **Vorschläge**. Sie listet jeden Detektor mit seinen Zählern und seiner Schätzung.

   - **Eine Woche stumm** oder **Stumm schalten** (bis du ihn wieder einschaltest) stoppt den Detektor für dich.
   - **Zähler zurücksetzen** leert die Zähler, damit der Detektor wieder neu erkundet.
:::

## Prüfen, ob es funktioniert

Öffne ein Fenster, das einen `binary_sensor` mit der Geräteklasse `window` hat, während die Vorhersage Regen zeigt. Innerhalb weniger Minuten zeigt der Eingang einen Vorschlag `window_rain`.

## Verwandte Seiten

::: cards
- [Konfiguration von `suggestions`](../configuration/marshal-yaml/suggestions.md)
- [Wiederkehrende Abläufe zu Skills machen](skill-proposals.md)
- [Wartung nach Nutzung verfolgen](maintenance.md)
- [Risikostufen](../reference/risk-tiers.md)
:::
