---
icon: fa-solid fa-screwdriver-wrench
description: "Lass dich nach einer Zahl von Druckstunden oder Zyklen erinnern, eine Düse, einen Filter oder Riemen zu wechseln, nicht nach einem festen Datum."
---

# Wartung nach Nutzung verfolgen

Lass dich nach einer Zahl von Druckstunden oder Zyklen erinnern, eine Düse, einen Filter oder Riemen zu wechseln, nicht nach einem festen Datum.

## Bevor du anfängst

- Ein Zähler, der nur steigt: ein konfigurierter [Moonraker-Drucker](integrations/printers-moonraker.md) (Druckstunden oder Filamentmeter) oder eine Home-Assistant-Entität oder -Statistik, zum Beispiel `sensor.dishwasher_cycles`.
- Zugriff auf `/config/marshal.yaml`.

## Schritte

::: steps
1. **Füge Objekte hinzu**

   Füge `marshal.yaml` den Schlüssel `maintenance.items` hinzu. Jedes Objekt hat eine ID, einen Namen, eine Quelle und ein Intervall in der Einheit des Zählers:

   ```yaml [/config/marshal.yaml]
   maintenance:
     items:
       voron_nozzle:
         name: Voron-Düse
         source: { printer: voron, metric: printHours }
         every: 300
         warnBefore: 20
       dishwasher_filter:
         name: Filter der Spülmaschine
         source: { ha: sensor.dishwasher_cycles }
         every: 60
   ```

   Starte Marshal neu. Der Start schlägt mit einer Meldung fehl, die den Schlüssel nennt, wenn ein Objekt ungültig ist, zum Beispiel wenn es einen nicht konfigurierten Drucker nennt. Alle Schlüssel stehen in der [Referenz von `maintenance`](../configuration/marshal-yaml/maintenance.md).

   > [!TIP]
   > Mit eingeschaltetem „Marshal beim Lernen helfen“ schlägt der [Vorschlag](suggestions.md) `maintenance_counter` Objekte für Zähler vor, die noch keines haben, mit einem Snippet zum Kopieren.

2. **Wisse, wie die Nutzung gezählt wird**

   Beim ersten Sehen nimmt Marshal den aktuellen Wert des Zählers als Startpunkt. Die Nutzung ist der aktuelle Wert des Zählers minus diesem Startpunkt. Fällt ein Zähler, zum Beispiel nach dem Zurücksetzen eines Sensors, zählt Marshal ab dem neuen Wert weiter.

   Marshal liest Zähler, wenn sich die Home-Assistant-Entität ändert, wenn ein Druck fertig ist und stündlich.

3. **Bekomme Erinnerungen**

   - Erreicht die Nutzung `every - warnBefore`, bekommst du eine `normal`-Benachrichtigung, dass das Objekt bald fällig ist.
   - Erreicht die Nutzung `every`, bekommst du eine `normal`-Benachrichtigung, dass es fällig ist.

   Start zeigt nach den Druckern eine Karte **Wartung**. Fällige Objekte kommen zuerst, dann bald fällige.

4. **Markiere ein Objekt als erledigt**

   Markiere das Objekt nach der Wartung als erledigt. Die Zählung beginnt beim aktuellen Wert des Zählers neu.

   - **Auf Start:** Klicke neben dem Objekt auf **Erledigt** und bestätige.
   - **Per Stimme oder Chat:** Sag zum Beispiel „Ich habe die Düse am Voron gewechselt“. Marshal ruft `maintenance.done` auf. In einem Durchlauf, der nicht vertrauenswürdige Inhalte gelesen hat, etwa Mail, braucht der Aufruf deine Bestätigung.

   Frag „welche Wartung ist fällig?“, um über `maintenance.status` den Stand jedes Objekts zu bekommen.
:::

## Prüfen, ob es funktioniert

Öffne Start. Die Karte **Wartung** listet jedes Objekt mit seiner Nutzung, zum Beispiel `186.4 / 300 h`.

## Verwandte Seiten

::: cards
- [Konfiguration von `maintenance`](../configuration/marshal-yaml/maintenance.md)
- [Werkzeuge: Wartung](../reference/tools.md#wartung-maintenance)
- [Vorschläge bekommen](suggestions.md)
- [Benachrichtigungen](notifications.md)
:::
