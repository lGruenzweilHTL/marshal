---
icon: fa-solid fa-database
description: "Marshals Regeln, um das EF-Core-Modell zu ändern und Migrationen anzulegen."
---

# Datenbankmigrationen

Marshals Regeln, um das EF-Core-Modell zu ändern. `dotnet-ef` ist ein lokales Tool (`dotnet tool restore`).

```sh
dotnet ef migrations add <Name> --project src/Marshal.Data --output-dir Migrations
```

## Regeln

- Alle Entitäten und Migrationen liegen in `src/Marshal.Data`, auch für Entitäten, die einem anderen Projekt gehören (Gedächtnis, Aufgaben). Namen sind snake_case über `EFCore.NamingConventions`.
- Jede Zeile, die einem Benutzer gehört, hat `owner_id`, auch in Kindtabellen, und bekommt den Abfragefilter pro Eigentümer. Lies „den Benutzer“ nie aus einer globalen Variable.
- Neue Daten mit Eigentümer bekommen einen Eigentumstest mit einem zweiten Fake-Benutzer (`TestDatabase` aus `Marshal.Testing`).
- Migrationen sind additiv und eingecheckt. Ändere nie eine ausgelieferte Migration. Marshal wendet ausstehende Migrationen beim Start an, nach einer Sicherung der SQLite-Datei.

## In derselben Änderung aktualisieren

- Das [Datenmodell](../diagrams/erd.md): das Snippet `erd-<area>-<part>.md`, das die Entität enthält, und die Tabelle der Constraints ([Diagramme pflegen](../diagrams/README.md)).
- Die [Referenz des Datenverzeichnisses](../reference/data-directory.md), wenn sich ändert, was in `/data` liegt.
- Die [Anleitung zum Aktualisieren](../installation/upgrading.md), wenn eine Migration Aufmerksamkeit beim Upgrade braucht.

## Verwandte Seiten

::: cards
- [Dateneigentum](../concepts/data-ownership.md)
- [Aktualisieren](../installation/upgrading.md)
:::
