---
icon: fa-solid fa-database
description: "Change the EF Core model and create the migration that goes with it."
---

# Database migrations

Change the EF Core model and create the migration that goes with it.

## Before you start

- Run `dotnet tool restore` once ([setup](setup.md)). The repository pins `dotnet-ef` as a local tool.
- Persistence is SQLite through EF Core. Names use snake_case through `EFCore.NamingConventions`.
- Entities and migrations live in `src/Marshal.Data`, even for entities that other projects own (for example memory).

## Rules

- Every user-owned row has an `owner_id`. Every read and write goes through the owner-scoped global query filters. Never read "the user" from a global.
- Write an ownership test with a second fake user for new owned data.
- Migrations are additive and committed. Do not edit a migration after it has shipped.

## Steps

::: steps
1. **Write the failing test**

   Use `TestDatabase` from `tests/Marshal.Testing` in `tests/Marshal.Data.Tests` or the owning project's tests.

2. **Change the model**

   Add or edit the entity and its configuration in `src/Marshal.Data`. Add `OwnerId` to user-owned entities and make sure the query filter applies.

3. **Create the migration**

   ```sh
   dotnet ef migrations add <Name> --project src/Marshal.Data --output-dir Migrations
   ```

   Review the generated `*.cs`, `*.Designer.cs` and `MarshalDbContextModelSnapshot.cs`. Check the SQL for `owner_id` columns and indexes.

4. **Update the ERD**

   Update the [data model](../diagrams/erd.md) in the same change that adds, removes or renames an entity, column or relationship: the `erDiagram` of the entity's area, and the table of constraints at the end of the page. Check the drawing in `npx clay-doc@5 dev`; see [maintaining the diagrams](../diagrams/README.md).

5. **Update the reference**

   If the change alters what lives in `/data`, update the [data directory reference](../reference/data-directory.md).
:::

## Check that it works

- `dotnet test --project tests/Marshal.Data.Tests` passes.
- A fresh database applies every migration.
- The ERD matches the model.

## Related

::: cards
- [Data ownership](../concepts/data-ownership.md)
- [Upgrading](../installation/upgrading.md)
:::
