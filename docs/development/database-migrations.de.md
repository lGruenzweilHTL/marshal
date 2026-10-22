---
icon: fa-solid fa-database
description: "Das EF-Core-Modell ändern und die passende Migration anlegen."
---

# Datenbankmigrationen

Das EF-Core-Modell ändern und die passende Migration anlegen.

## Bevor du anfängst

- Führ einmal `dotnet tool restore` aus ([Einrichtung](setup.md)). Das Repository pinnt `dotnet-ef` als lokales Werkzeug.
- Die Persistenz ist SQLite über EF Core. Namen sind snake_case über `EFCore.NamingConventions`.
- Entitäten und Migrationen liegen in `src/Marshal.Data`, auch für Entitäten, die anderen Projekten gehören (zum Beispiel dem Gedächtnis).

## Regeln

- Jede Zeile, die einem Benutzer gehört, hat eine `owner_id`. Jeder Lese- und Schreibzugriff läuft über die globalen, auf den Eigentümer beschränkten Abfragefilter. Lies „den Benutzer“ nie aus einer globalen Variable.
- Schreib für neue Daten mit Eigentümer einen Test zum Eigentum mit einem zweiten Fake-Benutzer.
- Migrationen sind additiv und werden committet. Bearbeite eine Migration nicht mehr, nachdem sie ausgeliefert wurde.

## Schritte

::: steps
1. **Den fehlschlagenden Test schreiben**

   Nutze `TestDatabase` aus `tests/Marshal.Testing` in `tests/Marshal.Data.Tests` oder in den Tests des Projekts, dem die Entität gehört.

2. **Das Modell ändern**

   Füg die Entität und ihre Konfiguration in `src/Marshal.Data` hinzu oder bearbeite sie. Ergänze `OwnerId` bei Entitäten, die einem Benutzer gehören, und stell sicher, dass der Abfragefilter greift.

3. **Die Migration anlegen**

   ```sh
   dotnet ef migrations add <Name> --project src/Marshal.Data --output-dir Migrations
   ```

   Prüfe die erzeugten Dateien `*.cs`, `*.Designer.cs` und `MarshalDbContextModelSnapshot.cs`. Prüfe das SQL auf Spalten `owner_id` und Indizes.

4. **Das ERD aktualisieren**

   Aktualisiere das [Datenmodell](../diagrams/erd.md) in derselben Änderung, die eine Entität, Spalte oder Beziehung hinzufügt, entfernt oder umbenennt: das `erDiagram` des Bereichs der Entität und die Tabelle der Einschränkungen am Ende der Seite. Prüfe die Zeichnung in `npx clay-doc@5 dev`; siehe [Die Diagramme pflegen](../diagrams/README.md).

5. **Die Referenz aktualisieren**

   Ändert die Änderung, was in `/data` liegt, aktualisiere die Referenz zum [Datenverzeichnis](../reference/data-directory.md).
:::

## Prüfen, ob es funktioniert

- `dotnet test --project tests/Marshal.Data.Tests` besteht.
- Eine frische Datenbank wendet jede Migration an.
- Das ERD passt zum Modell.

## Verwandte Seiten

::: cards
- [Datenbesitz](../concepts/data-ownership.md)
- [Aktualisieren](../installation/upgrading.md)
:::
